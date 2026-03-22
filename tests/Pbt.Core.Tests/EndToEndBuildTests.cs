using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

/// <summary>
/// End-to-end integration tests that exercise the full build pipeline:
/// YAML → ModelComposer → TOM Database → TMDL serialize → TMDL deserialize → TOM validation.
/// This verifies that all new features produce valid TMDL output.
/// </summary>
public class EndToEndBuildTests
{
    private readonly YamlSerializer _serializer = new();

    /// <summary>
    /// Creates a comprehensive project on disk exercising every feature:
    /// - Tables with partitions (Import/DirectQuery), column properties (IsKey, SummarizeBy, SortByColumn, DataCategory, Annotations)
    /// - Calculated columns, hierarchies, table-level measures
    /// - Relationship shorthand syntax
    /// - Referential integrity on relationships
    /// - Shared expressions
    /// - Calculation groups with items
    /// - Perspectives
    /// - Roles with RLS
    /// - Field parameters
    /// - Model-level measure overrides
    /// - Lineage tag management
    ///
    /// Then builds, serializes to TMDL, deserializes back, and validates.
    /// </summary>
    [Fact]
    public void FullBuild_AllFeatures_ShouldProduceValidTmdl()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pbt_e2e_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            // ── 1. Create project structure on disk ──
            CreateComprehensiveProject(tempPath);

            // ── 2. Load project ──
            var projectYaml = Path.Combine(tempPath, "project.yml");
            var project = _serializer.LoadFromFile<ProjectDefinition>(projectYaml);
            Assert.Equal("E2ETestProject", project.Name);
            Assert.Equal(1600, project.CompatibilityLevel);

            // ── 3. Load table registry ──
            var tablesPath = Path.Combine(tempPath, "tables");
            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);
            Assert.Equal(3, registry.Count); // Sales, Customers, DateDim

            // ── 4. Load model definition ──
            var modelPath = Path.Combine(tempPath, "models", "full_model.yaml");
            var modelDef = _serializer.LoadFromFile<ModelDefinition>(modelPath);
            Assert.Equal("FullTestModel", modelDef.Name);

            // ── 5. Validate project ──
            var validator = new Validator(_serializer);
            var validationResult = validator.ValidateProject(tempPath);
            Assert.True(validationResult.IsValid, $"Validation failed: {validationResult.FormatMessages()}");

            // ── 6. Compose TOM Database with lineage ──
            var lineageService = new LineageManifestService(_serializer);
            lineageService.LoadManifest(tempPath);

            var composer = new ModelComposer(registry);
            var database = composer.ComposeModel(modelDef, project.CompatibilityLevel, lineageService, project, tempPath);

            Assert.NotNull(database);
            Assert.NotNull(database.Model);

            // ── 7. Validate TOM model ──
            var tomValidation = validator.ValidateTomModel(database, modelDef.Name);
            Assert.True(tomValidation.IsValid, $"TOM validation failed: {tomValidation.FormatMessages()}");

            // ── 8. Assert model structure ──
            AssertModelStructure(database);

            // ── 9. Check lineage collision warnings ──
            Assert.Empty(lineageService.CollisionWarnings);
            Assert.True(lineageService.NewTagCount > 0, "Should have generated new lineage tags");

            // ── 10. Serialize to TMDL ──
            var tmdlOutputPath = Path.Combine(tempPath, "tmdl_output");
            Directory.CreateDirectory(tmdlOutputPath);
            TmdlSerializer.SerializeDatabaseToFolder(database, tmdlOutputPath);

            // Assert TMDL files created
            Assert.True(File.Exists(Path.Combine(tmdlOutputPath, "database.tmdl")), "database.tmdl missing");
            Assert.True(File.Exists(Path.Combine(tmdlOutputPath, "model.tmdl")), "model.tmdl missing");
            Assert.True(Directory.Exists(Path.Combine(tmdlOutputPath, "tables")), "tables/ directory missing");

            // ── 11. Deserialize from TMDL (round-trip validation) ──
            var deserializedDb = TmdlSerializer.DeserializeDatabaseFromFolder(tmdlOutputPath);

            Assert.Equal(database.Name, deserializedDb.Name);
            Assert.Equal(database.CompatibilityLevel, deserializedDb.CompatibilityLevel);
            Assert.Equal(database.Model.Tables.Count, deserializedDb.Model.Tables.Count);
            Assert.Equal(database.Model.Relationships.Count, deserializedDb.Model.Relationships.Count);

            // ── 12. Validate deserialized model ──
            var deserializedTomValidation = validator.ValidateTomModel(deserializedDb, modelDef.Name);
            Assert.True(deserializedTomValidation.IsValid,
                $"Deserialized TOM validation failed: {deserializedTomValidation.FormatMessages()}");

            // ── 13. Assert round-trip fidelity ──
            AssertRoundTripFidelity(database, deserializedDb);

            // ── 14. Save and reload lineage manifest ──
            lineageService.SaveManifest(tempPath);
            var lineageService2 = new LineageManifestService(_serializer);
            lineageService2.LoadManifest(tempPath);

            // Rebuild with existing manifest ─ all tags should be reused
            var database2 = composer.ComposeModel(modelDef, project.CompatibilityLevel, lineageService2, project, tempPath);
            Assert.Equal(0, lineageService2.NewTagCount);
            Assert.True(lineageService2.ExistingTagCount > 0);

            // Verify tags match
            foreach (var table in database.Model.Tables)
            {
                var table2 = database2.Model.Tables.Find(table.Name);
                Assert.NotNull(table2);
                Assert.Equal(table.LineageTag, table2.LineageTag);
            }

            // ── 15. Second TMDL round-trip with stable tags ──
            var tmdlOutputPath2 = Path.Combine(tempPath, "tmdl_output2");
            Directory.CreateDirectory(tmdlOutputPath2);
            TmdlSerializer.SerializeDatabaseToFolder(database2, tmdlOutputPath2);
            var deserializedDb2 = TmdlSerializer.DeserializeDatabaseFromFolder(tmdlOutputPath2);

            var finalValidation = validator.ValidateTomModel(deserializedDb2, modelDef.Name);
            Assert.True(finalValidation.IsValid,
                $"Final TOM validation failed: {finalValidation.FormatMessages()}");
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    /// <summary>
    /// Test dry-run mode: compose model but don't write files
    /// </summary>
    [Fact]
    public void DryRun_ShouldComposeWithoutWriting()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pbt_dryrun_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            CreateComprehensiveProject(tempPath);

            var project = _serializer.LoadFromFile<ProjectDefinition>(Path.Combine(tempPath, "project.yml"));
            var registry = new TableRegistry(_serializer);
            registry.LoadTables(Path.Combine(tempPath, "tables"));

            var modelDef = _serializer.LoadFromFile<ModelDefinition>(Path.Combine(tempPath, "models", "full_model.yaml"));

            var lineageService = new LineageManifestService(_serializer);
            var composer = new ModelComposer(registry);

            // Act - compose without writing
            var database = composer.ComposeModel(modelDef, project.CompatibilityLevel, lineageService, project, tempPath);

            // Assert - model is valid
            Assert.NotNull(database);
            Assert.NotNull(database.Model);

            var validator = new Validator(_serializer);
            var tomValidation = validator.ValidateTomModel(database, modelDef.Name);
            Assert.True(tomValidation.IsValid, tomValidation.FormatMessages());

            // Assert - no output directory created
            Assert.False(Directory.Exists(Path.Combine(tempPath, "target")));
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }
    }

    /// <summary>
    /// Test PBIP generation produces valid structure
    /// </summary>
    [Fact]
    public void PbipGeneration_ShouldProduceValidStructure()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pbt_pbip_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            CreateComprehensiveProject(tempPath);

            var project = _serializer.LoadFromFile<ProjectDefinition>(Path.Combine(tempPath, "project.yml"));
            var registry = new TableRegistry(_serializer);
            registry.LoadTables(Path.Combine(tempPath, "tables"));

            var modelDef = _serializer.LoadFromFile<ModelDefinition>(Path.Combine(tempPath, "models", "full_model.yaml"));

            var lineageService = new LineageManifestService(_serializer);
            var composer = new ModelComposer(registry);
            var database = composer.ComposeModel(modelDef, project.CompatibilityLevel, lineageService, project, tempPath);

            // Act - generate PBIP
            var targetPath = Path.Combine(tempPath, "target");
            PbipGenerator.GeneratePbipStructure(database, project.Name, targetPath);

            // Assert - PBIP structure
            var sanitizedName = FileNameSanitizer.Sanitize(project.Name);
            Assert.True(File.Exists(Path.Combine(targetPath, $"{sanitizedName}.pbip")), ".pbip file missing");
            Assert.True(Directory.Exists(Path.Combine(targetPath, $"{sanitizedName}.SemanticModel")), "SemanticModel dir missing");
            Assert.True(Directory.Exists(Path.Combine(targetPath, $"{sanitizedName}.Report")), "Report dir missing");

            // Verify TMDL files in SemanticModel
            var semanticModelPath = Path.Combine(targetPath, $"{sanitizedName}.SemanticModel");
            var definitionDir = Path.Combine(semanticModelPath, "definition");
            Assert.True(Directory.Exists(definitionDir), "definition/ directory missing");
            Assert.True(File.Exists(Path.Combine(definitionDir, "database.tmdl")), "database.tmdl missing in SemanticModel");

            // Round-trip validate the generated TMDL
            var deserializedDb = TmdlSerializer.DeserializeDatabaseFromFolder(definitionDir);
            var validator = new Validator(_serializer);
            var validation = validator.ValidateTomModel(deserializedDb, modelDef.Name);
            Assert.True(validation.IsValid, $"PBIP TMDL validation failed: {validation.FormatMessages()}");
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }
    }

    #region Project Creation

    private void CreateComprehensiveProject(string basePath)
    {
        var tablesPath = Path.Combine(basePath, "tables");
        var modelsPath = Path.Combine(basePath, "models");
        Directory.CreateDirectory(tablesPath);
        Directory.CreateDirectory(modelsPath);

        // ── project.yml ──
        var project = new ProjectDefinition
        {
            Name = "E2ETestProject",
            Description = "End-to-end test project exercising all features",
            CompatibilityLevel = 1600,
            Expressions = new List<ExpressionDefinition>
            {
                new()
                {
                    Name = "ServerName",
                    Kind = "M",
                    Expression = "\"localhost\" meta [IsParameterQuery=true, Type=\"Text\", IsParameterQueryRequired=true]",
                    Description = "Database server name"
                }
            }
        };
        _serializer.SaveToFile(project, Path.Combine(basePath, "project.yml"));

        // ── Sales table (partitions, column properties, measures) ──
        CreateSalesTable(tablesPath);

        // ── Customers table (hierarchies, calculated columns, annotations) ──
        CreateCustomersTable(tablesPath);

        // ── DateDim table (SortByColumn, DataCategory) ──
        CreateDateDimTable(tablesPath);

        // ── Model definition (all features) ──
        CreateFullModel(modelsPath);
    }

    private void CreateSalesTable(string tablesPath)
    {
        var salesYaml = @"
name: Sales
description: Sales fact table with partitions
partitions:
  - name: Sales_Historical
    mode: Import
    m_expression: |
      let
        Source = #table(
          {""OrderID"", ""OrderDate"", ""Amount"", ""CustomerID"", ""DateKey""},
          {
            {1, #date(2024, 1, 15), 1500.00, 101, 20240115},
            {2, #date(2024, 1, 16), 2300.50, 102, 20240116}
          }
        )
      in
        Source
  - name: Sales_Current
    mode: Import
    m_expression: |
      let
        Source = #table(
          {""OrderID"", ""OrderDate"", ""Amount"", ""CustomerID"", ""DateKey""},
          {
            {3, #date(2024, 6, 1), 950.75, 101, 20240601}
          }
        )
      in
        Source
columns:
  - name: OrderID
    type: Int64
    source_column: OrderID
    description: Unique order identifier
    is_key: true
    summarize_by: None
  - name: OrderDate
    type: DateTime
    source_column: OrderDate
    description: Date of the order
  - name: Amount
    type: Decimal
    source_column: Amount
    description: Order amount
    format_string: ""$#,##0.00""
    summarize_by: Sum
  - name: CustomerID
    type: Int64
    source_column: CustomerID
    description: Customer foreign key
    summarize_by: None
  - name: DateKey
    type: Int64
    source_column: DateKey
    description: Date dimension key
    summarize_by: None
    is_hidden: true
  - name: IsLargeOrder
    type: Boolean
    expression: ""IF([Amount] > 1000, TRUE(), FALSE())""
    description: Flag for orders over 1000
    display_folder: Flags
measures:
  - name: Total Sales
    table: Sales
    expression: SUM(Sales[Amount])
    format_string: ""$#,##0.00""
    display_folder: Sales Metrics
  - name: Order Count
    table: Sales
    expression: COUNTROWS(Sales)
    format_string: ""#,##0""
    display_folder: Sales Metrics
";
        File.WriteAllText(Path.Combine(tablesPath, "sales.yaml"), salesYaml);
    }

    private void CreateCustomersTable(string tablesPath)
    {
        var customersYaml = @"
name: Customers
description: Customer dimension table
m_expression: |
  let
    Source = #table(
      {""CustomerID"", ""CustomerName"", ""City"", ""Country"", ""Region""},
      {
        {101, ""Acme Corp"", ""New York"", ""USA"", ""North America""},
        {102, ""Tech Solutions"", ""London"", ""UK"", ""Europe""},
        {103, ""Global Industries"", ""Tokyo"", ""Japan"", ""Asia""}
      }
    )
  in
    Source
columns:
  - name: CustomerID
    type: Int64
    source_column: CustomerID
    description: Unique customer identifier
    is_key: true
    summarize_by: None
  - name: CustomerName
    type: String
    source_column: CustomerName
    description: Customer name
    data_category: Organization
  - name: City
    type: String
    source_column: City
    description: City
    data_category: City
  - name: Country
    type: String
    source_column: Country
    description: Country
    data_category: Country
    annotations:
      PBI_GeoEncoding: Country
  - name: Region
    type: String
    source_column: Region
    description: Geographic region
hierarchies:
  - name: Geography
    display_folder: Geo
    levels:
      - name: Region
        column: Region
      - name: Country
        column: Country
      - name: City
        column: City
";
        File.WriteAllText(Path.Combine(tablesPath, "customers.yaml"), customersYaml);
    }

    private void CreateDateDimTable(string tablesPath)
    {
        var dateDimYaml = @"
name: DateDim
description: Date dimension table
m_expression: |
  let
    Source = #table(
      {""DateKey"", ""Year"", ""MonthNum"", ""MonthName"", ""Quarter""},
      {
        {20240115, 2024, 1, ""January"", ""Q1""},
        {20240116, 2024, 1, ""January"", ""Q1""},
        {20240601, 2024, 6, ""June"", ""Q2""}
      }
    )
  in
    Source
columns:
  - name: DateKey
    type: Int64
    source_column: DateKey
    is_key: true
    summarize_by: None
  - name: Year
    type: Int64
    source_column: Year
    summarize_by: None
  - name: MonthNum
    type: Int64
    source_column: MonthNum
    is_hidden: true
    summarize_by: None
  - name: MonthName
    type: String
    source_column: MonthName
    sort_by_column: MonthNum
  - name: Quarter
    type: String
    source_column: Quarter
";
        File.WriteAllText(Path.Combine(tablesPath, "datedim.yaml"), dateDimYaml);
    }

    private void CreateFullModel(string modelsPath)
    {
        var modelYaml = @"
name: FullTestModel
description: Comprehensive model testing all features

tables:
  - ref: Sales
  - ref: Customers
  - ref: DateDim

relationships:
  - from_table: Sales
    from_column: CustomerID
    to_table: Customers
    to_column: CustomerID
    cardinality: ManyToOne
    cross_filter_direction: Both
    active: true
    rely_on_referential_integrity: true
  - from_table: Sales
    from_column: DateKey
    to_table: DateDim
    to_column: DateKey
    cardinality: ManyToOne
    cross_filter_direction: Single
    active: true

measures:
  - name: Average Order Value
    table: Sales
    expression: DIVIDE([Total Sales], [Order Count])
    format_string: ""$#,##0.00""
    display_folder: Sales Metrics
    description: Average value per order

expressions:
  - name: DatabaseName
    kind: M
    expression: '""TestDB"" meta [IsParameterQuery=true, Type=""Text"", IsParameterQueryRequired=true]'
    description: Database name parameter

calculation_groups:
  - name: Time Intelligence
    description: Time intelligence calculations
    precedence: 10
    columns:
      - name: Time Calculation
        type: String
        source_column: Name
    calculation_items:
      - name: Current
        expression: SELECTEDMEASURE()
        ordinal: 0
      - name: YTD
        expression: CALCULATE(SELECTEDMEASURE(), DATESYTD(DateDim[DateKey]))
        ordinal: 1
      - name: PY
        expression: CALCULATE(SELECTEDMEASURE(), SAMEPERIODLASTYEAR(DateDim[DateKey]))
        ordinal: 2

perspectives:
  - name: Sales Overview
    description: High-level sales view for executives
    tables:
      - Sales
      - Customers
    measures:
      - Total Sales
      - Order Count
      - Average Order Value

roles:
  - name: RegionManager
    description: Managers can see only their region data
    model_permission: Read
    table_permissions:
      - table: Customers
        filter_expression: '[Country] = ""USA""'

field_parameters:
  - name: Sales Metric
    description: Dynamic metric selector
    values:
      - name: Total Sales
        expression: NAMEOF('Sales'[Total Sales])
        ordinal: 0
      - name: Order Count
        expression: NAMEOF('Sales'[Order Count])
        ordinal: 1
";
        File.WriteAllText(Path.Combine(modelsPath, "full_model.yaml"), modelYaml);
    }

    #endregion

    #region Assertions

    private void AssertModelStructure(Database database)
    {
        var model = database.Model;

        // ── Tables ──
        // 3 regular tables + 1 calc group + 1 field parameter = 5
        Assert.Equal(5, model.Tables.Count);
        Assert.NotNull(model.Tables.Find("Sales"));
        Assert.NotNull(model.Tables.Find("Customers"));
        Assert.NotNull(model.Tables.Find("DateDim"));
        Assert.NotNull(model.Tables.Find("Time Intelligence"));
        Assert.NotNull(model.Tables.Find("Sales Metric"));

        // ── Sales table ──
        var salesTable = model.Tables.Find("Sales")!;
        Assert.Equal(6, salesTable.Columns.Count); // 5 data + 1 calculated
        Assert.Equal(2, salesTable.Partitions.Count); // Historical + Current

        // Verify partition modes
        Assert.Equal(ModeType.Import, salesTable.Partitions[0].Mode);

        // Verify column properties
        var orderIdCol = salesTable.Columns.Find("OrderID")!;
        Assert.True(orderIdCol.IsKey);
        Assert.Equal(AggregateFunction.None, orderIdCol.SummarizeBy);
        Assert.False(string.IsNullOrWhiteSpace(orderIdCol.LineageTag));

        var amountCol = salesTable.Columns.Find("Amount")!;
        Assert.Equal(AggregateFunction.Sum, amountCol.SummarizeBy);
        Assert.Equal("$#,##0.00", amountCol.FormatString);

        // Verify calculated column
        var isLargeOrder = salesTable.Columns.Find("IsLargeOrder");
        Assert.NotNull(isLargeOrder);
        Assert.IsType<CalculatedColumn>(isLargeOrder);
        Assert.Contains("IF([Amount]", ((CalculatedColumn)isLargeOrder).Expression);

        // Verify measures (table-level + model override)
        Assert.Equal(3, salesTable.Measures.Count);
        Assert.NotNull(salesTable.Measures.Find("Total Sales"));
        Assert.NotNull(salesTable.Measures.Find("Order Count"));
        Assert.NotNull(salesTable.Measures.Find("Average Order Value"));

        // ── Customers table ──
        var customersTable = model.Tables.Find("Customers")!;
        Assert.Equal(5, customersTable.Columns.Count);

        // Verify hierarchy
        Assert.Single(customersTable.Hierarchies);
        var geoHierarchy = customersTable.Hierarchies[0];
        Assert.Equal("Geography", geoHierarchy.Name);
        Assert.Equal(3, geoHierarchy.Levels.Count);
        Assert.Equal("Region", geoHierarchy.Levels[0].Name);
        Assert.Equal("Country", geoHierarchy.Levels[1].Name);
        Assert.Equal("City", geoHierarchy.Levels[2].Name);

        // Verify data category
        var cityCol = customersTable.Columns.Find("City")!;
        Assert.Equal("City", cityCol.DataCategory);

        // Verify annotations
        var countryCol = customersTable.Columns.Find("Country")!;
        Assert.Contains(countryCol.Annotations, a => a.Name == "PBI_GeoEncoding" && a.Value == "Country");

        // ── DateDim table ──
        var dateDimTable = model.Tables.Find("DateDim")!;
        Assert.Equal(5, dateDimTable.Columns.Count);

        // Verify SortByColumn
        var monthNameCol = dateDimTable.Columns.Find("MonthName")!;
        var monthNumCol = dateDimTable.Columns.Find("MonthNum")!;
        Assert.Equal(monthNumCol, monthNameCol.SortByColumn);

        // ── Relationships ──
        Assert.Equal(2, model.Relationships.Count);

        var salesCustomerRel = model.Relationships.Cast<SingleColumnRelationship>()
            .First(r => r.ToColumn.Table.Name == "Customers");
        Assert.Equal("Sales", salesCustomerRel.FromColumn.Table.Name);
        Assert.Equal("CustomerID", salesCustomerRel.FromColumn.Name);
        Assert.Equal(CrossFilteringBehavior.BothDirections, salesCustomerRel.CrossFilteringBehavior);
        Assert.True(salesCustomerRel.RelyOnReferentialIntegrity);

        var salesDateRel = model.Relationships.Cast<SingleColumnRelationship>()
            .First(r => r.ToColumn.Table.Name == "DateDim");
        Assert.Equal(CrossFilteringBehavior.OneDirection, salesDateRel.CrossFilteringBehavior);

        // ── Calculation group ──
        var calcGroupTable = model.Tables.Find("Time Intelligence")!;
        Assert.NotNull(calcGroupTable.CalculationGroup);
        Assert.Equal(3, calcGroupTable.CalculationGroup.CalculationItems.Count);
        Assert.Equal(10, calcGroupTable.CalculationGroup.Precedence);

        var ytdItem = calcGroupTable.CalculationGroup.CalculationItems
            .First(i => i.Name == "YTD");
        Assert.Contains("DATESYTD", ytdItem.Expression);

        // ── Field parameter ──
        var fieldParamTable = model.Tables.Find("Sales Metric")!;
        Assert.Contains(fieldParamTable.Annotations, a => a.Name == "ParameterMetadata");
        Assert.Equal(3, fieldParamTable.Columns.Count); // Value, Fields, Order

        // ── Perspectives ──
        Assert.Single(model.Perspectives);
        var perspective = model.Perspectives[0];
        Assert.Equal("Sales Overview", perspective.Name);
        Assert.Equal(2, perspective.PerspectiveTables.Count);

        // ── Roles ──
        Assert.Single(model.Roles);
        var role = model.Roles[0];
        Assert.Equal("RegionManager", role.Name);
        Assert.Equal(ModelPermission.Read, role.ModelPermission);
        Assert.Single(role.TablePermissions);
        Assert.Contains("USA", role.TablePermissions[0].FilterExpression);

        // ── Shared expressions ──
        Assert.True(model.Expressions.Count >= 1, "Should have at least one shared expression");

        // ── Lineage tags ──
        // Every table, column, measure, hierarchy should have lineage tags
        foreach (var table in model.Tables)
        {
            Assert.False(string.IsNullOrWhiteSpace(table.LineageTag),
                $"Table '{table.Name}' missing lineage tag");

            foreach (var col in table.Columns)
            {
                Assert.False(string.IsNullOrWhiteSpace(col.LineageTag),
                    $"Column '{table.Name}.{col.Name}' missing lineage tag");
            }

            foreach (var measure in table.Measures)
            {
                Assert.False(string.IsNullOrWhiteSpace(measure.LineageTag),
                    $"Measure '{table.Name}.[{measure.Name}]' missing lineage tag");
            }

            foreach (var hierarchy in table.Hierarchies)
            {
                Assert.False(string.IsNullOrWhiteSpace(hierarchy.LineageTag),
                    $"Hierarchy '{table.Name}.{hierarchy.Name}' missing lineage tag");
            }
        }
    }

    private void AssertRoundTripFidelity(Database original, Database deserialized)
    {
        // Tables
        Assert.Equal(original.Model.Tables.Count, deserialized.Model.Tables.Count);

        foreach (var origTable in original.Model.Tables)
        {
            var deserTable = deserialized.Model.Tables.Find(origTable.Name);
            Assert.NotNull(deserTable);

            // Columns
            Assert.Equal(origTable.Columns.Count, deserTable.Columns.Count);
            foreach (var origCol in origTable.Columns)
            {
                var deserCol = deserTable.Columns.Find(origCol.Name);
                Assert.NotNull(deserCol);
                Assert.Equal(origCol.DataType, deserCol.DataType);
                Assert.Equal(origCol.IsHidden, deserCol.IsHidden);
                Assert.Equal(origCol.LineageTag, deserCol.LineageTag);
            }

            // Measures
            Assert.Equal(origTable.Measures.Count, deserTable.Measures.Count);
            foreach (var origMeasure in origTable.Measures)
            {
                var deserMeasure = deserTable.Measures.Find(origMeasure.Name);
                Assert.NotNull(deserMeasure);
                Assert.Equal(origMeasure.Expression, deserMeasure.Expression);
                Assert.Equal(origMeasure.LineageTag, deserMeasure.LineageTag);
            }

            // Hierarchies
            Assert.Equal(origTable.Hierarchies.Count, deserTable.Hierarchies.Count);

            // Partitions - TMDL may add default partitions for calc groups/calculated tables
            // so we only assert >= original count
            Assert.True(deserTable.Partitions.Count >= origTable.Partitions.Count,
                $"Table '{origTable.Name}': expected at least {origTable.Partitions.Count} partitions, got {deserTable.Partitions.Count}");
        }

        // Relationships
        Assert.Equal(original.Model.Relationships.Count, deserialized.Model.Relationships.Count);

        // Perspectives
        Assert.Equal(original.Model.Perspectives.Count, deserialized.Model.Perspectives.Count);

        // Roles
        Assert.Equal(original.Model.Roles.Count, deserialized.Model.Roles.Count);
    }

    #endregion
}
