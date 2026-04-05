using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

/// <summary>
/// Integration tests using the example project
/// </summary>
public class IntegrationTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void LoadExampleProject_ShouldLoadAllTablesAndModels()
    {
        // Arrange
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            // Skip if not in project directory
            return;
        }

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath))
        {
            // Skip if example project doesn't exist
            return;
        }

        var tablesPath = Path.Combine(exampleProjectPath, "tables");
        var modelsPath = Path.Combine(exampleProjectPath, "models");

        // Act - Load model (which now carries project-level configuration)
        var salesModelPath = Path.Combine(modelsPath, "sales_model.yaml");
        var model = _serializer.LoadFromFile<ModelDefinition>(salesModelPath);

        // Act - Load table registry
        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Assert - Model carries project-level config
        Assert.Equal("SalesAnalytics", model.Name);
        Assert.Equal(1700, model.CompatibilityLevel);

        // Assert - Tables loaded
        Assert.Equal(3, registry.Count);
        Assert.True(registry.ContainsTable("Sales"));
        Assert.True(registry.ContainsTable("Customers"));
        Assert.True(registry.ContainsTable("DateDim"));

        // Assert - Sales table details (partitions, calculated columns, column properties)
        var salesTable = registry.GetTable("Sales");
        Assert.Equal("Sales", salesTable.Name);
        Assert.NotNull(salesTable.Partitions);
        Assert.Equal(2, salesTable.Partitions.Count);
        Assert.Equal("Sales_Historical", salesTable.Partitions[0].Name);
        Assert.Equal("Import", salesTable.Partitions[0].Mode);
        Assert.Equal(6, salesTable.Columns.Count); // 5 data + 1 calculated
        Assert.Contains(salesTable.Columns, c => c.Name == "IsLargeOrder" && c.Expression != null);
        Assert.Contains(salesTable.Columns, c => c.Name == "OrderID" && c.IsKey == true);
        Assert.Contains(salesTable.Columns, c => c.Name == "Amount" && c.SummarizeBy == "Sum");

        // Assert - Customers table details (hierarchy, data categories, annotations)
        var customersTable = registry.GetTable("Customers");
        Assert.Equal("Customers", customersTable.Name);
        Assert.Equal(5, customersTable.Columns.Count);
        Assert.Single(customersTable.Hierarchies);
        Assert.Equal("Geography", customersTable.Hierarchies[0].Name);
        Assert.Equal(3, customersTable.Hierarchies[0].Levels.Count);
        Assert.Contains(customersTable.Columns, c => c.Name == "City" && c.DataCategory == "City");
        Assert.Contains(customersTable.Columns, c => c.Name == "Country" && c.Annotations != null && c.Annotations.ContainsKey("PBI_GeoEncoding"));

        // Assert - DateDim table details (sort by column)
        var dateDimTable = registry.GetTable("DateDim");
        Assert.Equal("DateDim", dateDimTable.Name);
        Assert.Equal(5, dateDimTable.Columns.Count);
        Assert.Contains(dateDimTable.Columns, c => c.Name == "MonthName" && c.SortByColumn == "MonthNum");
        Assert.Contains(dateDimTable.Columns, c => c.Name == "MonthNum" && c.IsHidden == true);

        // Assert - Model content
        Assert.Equal(3, model.Tables.Count);
        Assert.Contains(model.Tables, t => t.Ref == "Sales");
        Assert.Contains(model.Tables, t => t.Ref == "Customers");
        Assert.Contains(model.Tables, t => t.Ref == "DateDim");

        // Assert - Relationships
        Assert.Equal(2, model.Relationships.Count);
        var custRelationship = model.Relationships.First(r => r.ToTable == "Customers");
        Assert.Equal("Sales", custRelationship.FromTable);
        Assert.Equal("CustomerID", custRelationship.FromColumn);
        Assert.Equal("ManyToOne", custRelationship.Cardinality);
        Assert.Equal("Both", custRelationship.CrossFilterDirection);
        Assert.True(custRelationship.RelyOnReferentialIntegrity);

        var dateRelationship = model.Relationships.First(r => r.ToTable == "DateDim");
        Assert.Equal("Sales", dateRelationship.FromTable);
        Assert.Equal("DateKey", dateRelationship.FromColumn);

        // Assert - Model-level measures
        Assert.Single(model.Measures);
        Assert.Contains(model.Measures, m => m.Name == "Average Order Value");

        // Assert - Model expressions
        Assert.NotNull(model.Expressions);
        Assert.Contains(model.Expressions, e => e.Name == "DatabaseName");

        // Assert - Calculation groups
        Assert.NotNull(model.CalculationGroups);
        Assert.Single(model.CalculationGroups);
        Assert.Equal("Time Intelligence", model.CalculationGroups[0].Name);
        Assert.Equal(3, model.CalculationGroups[0].CalculationItems.Count);

        // Assert - Perspectives
        Assert.NotNull(model.Perspectives);
        Assert.Single(model.Perspectives);
        Assert.Equal("Sales Overview", model.Perspectives[0].Name);

        // Assert - Roles
        Assert.NotNull(model.Roles);
        Assert.Single(model.Roles);
        Assert.Equal("RegionManager", model.Roles[0].Name);

        // Assert - Field parameters
        Assert.NotNull(model.FieldParameters);
        Assert.Single(model.FieldParameters);
        Assert.Equal("Sales Metric", model.FieldParameters[0].Name);
    }

    [Fact]
    public void ValidateExampleProject_AllTableReferencesExist()
    {
        // Arrange
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            return;
        }

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath))
        {
            return;
        }

        var tablesPath = Path.Combine(exampleProjectPath, "tables");
        var modelsPath = Path.Combine(exampleProjectPath, "models");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        var salesModelPath = Path.Combine(modelsPath, "sales_model.yaml");
        var model = _serializer.LoadFromFile<ModelDefinition>(salesModelPath);

        // Act & Assert - All table references should exist
        foreach (var tableRef in model.Tables)
        {
            Assert.True(registry.ContainsTable(tableRef.Ref),
                $"Table reference '{tableRef.Ref}' not found in registry");
        }

        // Act & Assert - All relationship tables should exist
        foreach (var rel in model.Relationships)
        {
            Assert.True(registry.ContainsTable(rel.FromTable),
                $"Relationship from table '{rel.FromTable}' not found in registry");
            Assert.True(registry.ContainsTable(rel.ToTable),
                $"Relationship to table '{rel.ToTable}' not found in registry");
        }

        // Act & Assert - All measure tables should exist
        foreach (var measure in model.Measures)
        {
            Assert.True(registry.ContainsTable(measure.Table),
                $"Measure table '{measure.Table}' not found in registry");
        }
    }

    /// <summary>
    /// Full end-to-end pipeline test using the sample project:
    /// YAML tables → TableRegistry → Validate → Compose TOM → TMDL serialize →
    /// TMDL deserialize → Validate round-trip → PBIP generation → Lineage stability.
    /// </summary>
    [Fact]
    public void SampleProject_FullEndToEnd_ShouldBuildAndRoundTrip()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null) return;

        var sampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(sampleProjectPath)) return;

        var tablesPath = Path.Combine(sampleProjectPath, "tables");
        var modelsPath = Path.Combine(sampleProjectPath, "models");

        // ── 1. Load tables into registry ──
        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        Assert.Equal(2, registry.Count);
        Assert.True(registry.ContainsTable("Sales"));
        Assert.True(registry.ContainsTable("Customers"));

        // Verify Sales table structure
        var salesDef = registry.GetTable("Sales");
        Assert.Equal(4, salesDef.Columns.Count);
        Assert.Contains(salesDef.Columns, c => c.Name == "OrderID" && c.Type == "Int64");
        Assert.Contains(salesDef.Columns, c => c.Name == "OrderDate" && c.Type == "DateTime");
        Assert.Contains(salesDef.Columns, c => c.Name == "Amount" && c.Type == "Decimal");
        Assert.Contains(salesDef.Columns, c => c.Name == "CustomerID" && c.Type == "Int64");
        Assert.NotNull(salesDef.MExpression);

        // Verify Customers table structure
        var customersDef = registry.GetTable("Customers");
        Assert.Equal(4, customersDef.Columns.Count);
        Assert.Contains(customersDef.Columns, c => c.Name == "CustomerID" && c.Type == "Int64");
        Assert.Contains(customersDef.Columns, c => c.Name == "CustomerName" && c.Type == "String");
        Assert.Contains(customersDef.Columns, c => c.Name == "City" && c.Type == "String");
        Assert.Contains(customersDef.Columns, c => c.Name == "Country" && c.Type == "String");
        Assert.NotNull(customersDef.MExpression);

        // ── 2. Load model definition ──
        var modelPath = Path.Combine(modelsPath, "sales_model.yaml");
        var modelDef = _serializer.LoadFromFile<ModelDefinition>(modelPath);
        Assert.Equal("SalesAnalytics", modelDef.Name);
        Assert.Equal(2, modelDef.Tables.Count);
        Assert.Single(modelDef.Relationships);
        Assert.Equal(3, modelDef.Measures.Count);

        // ── 3. Validate the project ──
        var validator = new Validator(_serializer);
        var validationResult = validator.ValidateProject(sampleProjectPath);
        Assert.True(validationResult.IsValid,
            $"Sample project validation failed: {validationResult.FormatMessages()}");

        // ── 4. Compose TOM Database with lineage ──
        var lineageService = new LineageManifestService(_serializer);
        var composer = new ModelComposer(registry);
        var database = composer.ComposeModel(modelDef, lineageService, sampleProjectPath);

        Assert.NotNull(database);
        Assert.NotNull(database.Model);

        // ── 5. Validate TOM model ──
        var tomValidation = validator.ValidateTomModel(database, modelDef.Name);
        Assert.True(tomValidation.IsValid,
            $"TOM validation failed: {tomValidation.FormatMessages()}");

        // ── 6. Verify composed model structure ──
        var model = database.Model;

        // Tables
        Assert.Equal(2, model.Tables.Count);
        var salesTable = model.Tables.Find("Sales");
        var customersTable = model.Tables.Find("Customers");
        Assert.NotNull(salesTable);
        Assert.NotNull(customersTable);

        // Sales columns
        Assert.Equal(4, salesTable!.Columns.Count);
        Assert.NotNull(salesTable.Columns.Find("OrderID"));
        Assert.NotNull(salesTable.Columns.Find("OrderDate"));
        Assert.NotNull(salesTable.Columns.Find("Amount"));
        Assert.NotNull(salesTable.Columns.Find("CustomerID"));

        // Customers columns
        Assert.Equal(4, customersTable!.Columns.Count);
        Assert.NotNull(customersTable.Columns.Find("CustomerID"));
        Assert.NotNull(customersTable.Columns.Find("CustomerName"));
        Assert.NotNull(customersTable.Columns.Find("City"));
        Assert.NotNull(customersTable.Columns.Find("Country"));

        // Relationship
        Assert.Single(model.Relationships);
        var rel = model.Relationships.Cast<Microsoft.AnalysisServices.Tabular.SingleColumnRelationship>().First();
        Assert.Equal("Sales", rel.FromColumn.Table.Name);
        Assert.Equal("CustomerID", rel.FromColumn.Name);
        Assert.Equal("Customers", rel.ToColumn.Table.Name);
        Assert.Equal("CustomerID", rel.ToColumn.Name);
        Assert.Equal(Microsoft.AnalysisServices.Tabular.RelationshipEndCardinality.Many, rel.FromCardinality);
        Assert.Equal(Microsoft.AnalysisServices.Tabular.CrossFilteringBehavior.BothDirections, rel.CrossFilteringBehavior);

        // Measures (model-level measures placed on Sales table)
        Assert.Equal(3, salesTable.Measures.Count);
        Assert.NotNull(salesTable.Measures.Find("Total Sales"));
        Assert.NotNull(salesTable.Measures.Find("Number of Orders"));
        Assert.NotNull(salesTable.Measures.Find("Average Order Value"));

        // Lineage tags present on all objects
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
                    $"Measure '{measure.Name}' missing lineage tag");
            }
        }

        // ── 7. Serialize to TMDL and round-trip ──
        var tmdlOutputPath = Path.Combine(Path.GetTempPath(), $"pbt_sample_e2e_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmdlOutputPath);

        try
        {
            TmdlSerializer.SerializeDatabaseToFolder(database, tmdlOutputPath);

            // Verify TMDL files created
            Assert.True(File.Exists(Path.Combine(tmdlOutputPath, "database.tmdl")));
            Assert.True(File.Exists(Path.Combine(tmdlOutputPath, "model.tmdl")));
            Assert.True(Directory.Exists(Path.Combine(tmdlOutputPath, "tables")));

            // Deserialize back
            var deserializedDb = TmdlSerializer.DeserializeDatabaseFromFolder(tmdlOutputPath);

            Assert.Equal(database.Name, deserializedDb.Name);
            Assert.Equal(database.Model.Tables.Count, deserializedDb.Model.Tables.Count);
            Assert.Equal(database.Model.Relationships.Count, deserializedDb.Model.Relationships.Count);

            // Validate deserialized model
            var roundTripValidation = validator.ValidateTomModel(deserializedDb, modelDef.Name);
            Assert.True(roundTripValidation.IsValid,
                $"Round-trip TOM validation failed: {roundTripValidation.FormatMessages()}");

            // Verify round-trip fidelity for each table
            foreach (var origTable in database.Model.Tables)
            {
                var rtTable = deserializedDb.Model.Tables.Find(origTable.Name);
                Assert.NotNull(rtTable);
                Assert.Equal(origTable.Columns.Count, rtTable!.Columns.Count);
                Assert.Equal(origTable.Measures.Count, rtTable.Measures.Count);

                foreach (var origCol in origTable.Columns)
                {
                    var rtCol = rtTable.Columns.Find(origCol.Name);
                    Assert.NotNull(rtCol);
                    Assert.Equal(origCol.DataType, rtCol!.DataType);
                    Assert.Equal(origCol.LineageTag, rtCol.LineageTag);
                }

                foreach (var origMeasure in origTable.Measures)
                {
                    var rtMeasure = rtTable.Measures.Find(origMeasure.Name);
                    Assert.NotNull(rtMeasure);
                    Assert.Equal(origMeasure.Expression, rtMeasure!.Expression);
                    Assert.Equal(origMeasure.LineageTag, rtMeasure.LineageTag);
                }
            }

            // ── 8. Generate PBIP structure ──
            var pbipOutputPath = Path.Combine(Path.GetTempPath(), $"pbt_sample_pbip_{Guid.NewGuid()}");
            try
            {
                PbipGenerator.GeneratePbipStructure(database, modelDef.Name, pbipOutputPath);

                var sanitizedName = FileNameSanitizer.Sanitize(modelDef.Name);
                Assert.True(File.Exists(Path.Combine(pbipOutputPath, $"{sanitizedName}.pbip")));
                Assert.True(Directory.Exists(Path.Combine(pbipOutputPath, $"{sanitizedName}.SemanticModel")));
                Assert.True(Directory.Exists(Path.Combine(pbipOutputPath, $"{sanitizedName}.Report")));

                // Validate TMDL within PBIP
                var definitionDir = Path.Combine(pbipOutputPath, $"{sanitizedName}.SemanticModel", "definition");
                Assert.True(Directory.Exists(definitionDir));

                var pbipDb = TmdlSerializer.DeserializeDatabaseFromFolder(definitionDir);
                var pbipValidation = validator.ValidateTomModel(pbipDb, modelDef.Name);
                Assert.True(pbipValidation.IsValid,
                    $"PBIP TMDL validation failed: {pbipValidation.FormatMessages()}");
            }
            finally
            {
                if (Directory.Exists(pbipOutputPath))
                    Directory.Delete(pbipOutputPath, true);
            }

            // ── 9. Verify lineage tag stability across rebuilds ──
            var lineageService2 = new LineageManifestService(_serializer);
            // Recompose with fresh lineage service (simulates rebuild without saved manifest)
            var database2 = composer.ComposeModel(modelDef, lineageService2, sampleProjectPath);

            // Tags should be deterministic (same inputs = same tags)
            foreach (var table in database.Model.Tables)
            {
                var table2 = database2.Model.Tables.Find(table.Name);
                Assert.NotNull(table2);
                Assert.Equal(table.LineageTag, table2!.LineageTag);

                foreach (var col in table.Columns)
                {
                    var col2 = table2.Columns.Find(col.Name);
                    Assert.NotNull(col2);
                    Assert.Equal(col.LineageTag, col2!.LineageTag);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tmdlOutputPath))
                Directory.Delete(tmdlOutputPath, true);
        }
    }

    /// <summary>
    /// Validates that the sample project column properties (data_category, summarize_by,
    /// is_key, format_string) are correctly composed into the TOM model.
    /// </summary>
    [Fact]
    public void SampleProject_ColumnProperties_ShouldBeComposedCorrectly()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null) return;

        var sampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(sampleProjectPath)) return;

        var tablesPath = Path.Combine(sampleProjectPath, "tables");
        var modelsPath = Path.Combine(sampleProjectPath, "models");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        var modelDef = _serializer.LoadFromFile<ModelDefinition>(
            Path.Combine(modelsPath, "sales_model.yaml"));

        var lineageService = new LineageManifestService(_serializer);
        var composer = new ModelComposer(registry);
        var database = composer.ComposeModel(modelDef, lineageService, sampleProjectPath);

        var model = database.Model;

        // Sales table column properties
        var salesTable = model.Tables.Find("Sales")!;
        var orderIdCol = salesTable.Columns.Find("OrderID")!;
        Assert.True(orderIdCol.IsKey);
        Assert.Equal(Microsoft.AnalysisServices.Tabular.AggregateFunction.None, orderIdCol.SummarizeBy);

        var amountCol = salesTable.Columns.Find("Amount")!;
        Assert.Equal("$#,##0.00", amountCol.FormatString);
        Assert.Equal(Microsoft.AnalysisServices.Tabular.AggregateFunction.Sum, amountCol.SummarizeBy);

        // Customers table column properties
        var customersTable = model.Tables.Find("Customers")!;
        var customerIdCol = customersTable.Columns.Find("CustomerID")!;
        Assert.True(customerIdCol.IsKey);

        var cityCol = customersTable.Columns.Find("City")!;
        Assert.Equal("City", cityCol.DataCategory);

        var countryCol = customersTable.Columns.Find("Country")!;
        Assert.Equal("Country", countryCol.DataCategory);
    }

    private string? FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "pbicomposer.sln")))
            {
                return directory;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }
        return null;
    }
}
