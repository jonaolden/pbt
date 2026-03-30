using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

/// <summary>
/// Full pipeline tests against the on-disk example project.
/// Exercises the entire build path: YAML load → validate → compose → TMDL serialize
/// → TMDL deserialize (round-trip validation) → TOM validation.
/// This is the definitive "does the example project produce valid TMDL?" test.
/// </summary>
public class ExampleProjectFullPipelineTests
{
    private readonly YamlSerializer _serializer = new();

    /// <summary>
    /// Full pipeline: load model via AssetLoader, validate, compose with lineage,
    /// serialize to TMDL, deserialize back, and validate the round-tripped model.
    /// </summary>
    [Fact]
    public void ExampleProject_FullPipeline_ShouldProduceValidTmdl()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null) return;

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath)) return;

        var tmdlOutputPath = Path.Combine(Path.GetTempPath(), $"pbt_example_pipeline_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmdlOutputPath);

        try
        {
            // ── 1. Load model and resolve asset paths ──
            var assetLoader = new AssetLoader(_serializer);
            var modelFiles = assetLoader.FindModelFiles(exampleProjectPath);
            Assert.Single(modelFiles);

            var modelDef = _serializer.LoadFromFile<ModelDefinition>(modelFiles[0]);
            Assert.Equal("SalesAnalytics", modelDef.Name);
            Assert.Equal(1700, modelDef.CompatibilityLevel);

            var assetPaths = assetLoader.ResolveAssetPaths(modelDef, exampleProjectPath);
            Assert.True(assetPaths.TablePaths.Count > 0, "Should have table paths");

            // ── 2. Validate project ──
            var validator = new Validator(_serializer);
            var validationResult = validator.ValidateProject(exampleProjectPath);
            Assert.True(validationResult.IsValid, $"Validation failed: {validationResult.FormatMessages()}");

            // ── 3. Load table registry ──
            var registry = assetLoader.CreateTableRegistry(assetPaths);
            Assert.Equal(3, registry.Count);

            // ── 4. Setup lineage service ──
            var lineageService = new LineageManifestService(_serializer);
            lineageService.LoadManifest(exampleProjectPath);

            // ── 5. Compose TOM Database ──
            var composer = new ModelComposer(registry);
            var database = composer.ComposeModel(
                modelDef, lineageService, exampleProjectPath);

            Assert.NotNull(database);
            Assert.NotNull(database.Model);

            // ── 6. Validate TOM model ──
            var tomValidation = validator.ValidateTomModel(database, modelDef.Name);
            Assert.True(tomValidation.IsValid, $"TOM validation failed: {tomValidation.FormatMessages()}");

            // ── 7. Assert full model structure ──
            var model = database.Model;

            // 3 regular tables + Time Intelligence calc group + Sales Metric field param = 5
            Assert.Equal(5, model.Tables.Count);

            // Sales: 6 columns, 2 partitions, 3 measures (2 table-level + 1 model-level)
            var sales = model.Tables.Find("Sales")!;
            Assert.Equal(6, sales.Columns.Count);
            Assert.Equal(2, sales.Partitions.Count);
            Assert.Equal(3, sales.Measures.Count);
            Assert.IsType<CalculatedColumn>(sales.Columns.Find("IsLargeOrder"));
            Assert.True(sales.Columns.Find("OrderID")!.IsKey);
            Assert.Equal(AggregateFunction.Sum, sales.Columns.Find("Amount")!.SummarizeBy);

            // Customers: 5 columns, 1 hierarchy, data categories, annotations
            var customers = model.Tables.Find("Customers")!;
            Assert.Equal(5, customers.Columns.Count);
            Assert.Single(customers.Hierarchies);
            Assert.Equal("Geography", customers.Hierarchies[0].Name);
            Assert.Equal(3, customers.Hierarchies[0].Levels.Count);
            Assert.Equal("City", customers.Columns.Find("City")!.DataCategory);
            Assert.Contains(customers.Columns.Find("Country")!.Annotations,
                a => a.Name == "PBI_GeoEncoding" && a.Value == "Country");

            // DateDim: sort by column
            var dateDim = model.Tables.Find("DateDim")!;
            Assert.Equal(dateDim.Columns.Find("MonthNum"), dateDim.Columns.Find("MonthName")!.SortByColumn);

            // Relationships: 2 (Sales→Customers, Sales→DateDim)
            Assert.Equal(2, model.Relationships.Count);
            var custRel = model.Relationships.Cast<SingleColumnRelationship>()
                .First(r => r.ToColumn.Table.Name == "Customers");
            Assert.Equal(CrossFilteringBehavior.BothDirections, custRel.CrossFilteringBehavior);
            Assert.True(custRel.RelyOnReferentialIntegrity);

            // Calculation group
            var calcGroup = model.Tables.Find("Time Intelligence")!;
            Assert.NotNull(calcGroup.CalculationGroup);
            Assert.Equal(3, calcGroup.CalculationGroup.CalculationItems.Count);
            Assert.Equal(10, calcGroup.CalculationGroup.Precedence);

            // Perspectives, Roles
            Assert.Single(model.Perspectives);
            Assert.Equal("Sales Overview", model.Perspectives[0].Name);
            Assert.Single(model.Roles);
            Assert.Equal("RegionManager", model.Roles[0].Name);
            Assert.Equal(ModelPermission.Read, model.Roles[0].ModelPermission);

            // Field parameter
            var fieldParam = model.Tables.Find("Sales Metric")!;
            Assert.Contains(fieldParam.Annotations, a => a.Name == "ParameterMetadata");

            // Shared expressions (model-level DatabaseName)
            Assert.True(model.Expressions.Count >= 1);

            // Lineage: all objects should have tags
            Assert.Empty(lineageService.CollisionWarnings);
            AssertAllLineageTags(model);

            // ── 8. Serialize to TMDL ──
            TmdlSerializer.SerializeDatabaseToFolder(database, tmdlOutputPath);

            Assert.True(File.Exists(Path.Combine(tmdlOutputPath, "database.tmdl")));
            Assert.True(File.Exists(Path.Combine(tmdlOutputPath, "model.tmdl")));
            Assert.True(Directory.Exists(Path.Combine(tmdlOutputPath, "tables")));

            // Verify TMDL content samples
            var salesTmdl = File.ReadAllText(Path.Combine(tmdlOutputPath, "tables", "Sales.tmdl"));
            Assert.Contains("table Sales", salesTmdl);
            Assert.Contains("measure 'Total Sales'", salesTmdl);
            Assert.Contains("column IsLargeOrder", salesTmdl);

            var customersTmdl = File.ReadAllText(Path.Combine(tmdlOutputPath, "tables", "Customers.tmdl"));
            Assert.Contains("hierarchy Geography", customersTmdl);

            // ── 9. Deserialize from TMDL (round-trip) ──
            var deserializedDb = TmdlSerializer.DeserializeDatabaseFromFolder(tmdlOutputPath);

            Assert.Equal(database.Name, deserializedDb.Name);
            Assert.Equal(database.CompatibilityLevel, deserializedDb.CompatibilityLevel);
            Assert.Equal(database.Model.Tables.Count, deserializedDb.Model.Tables.Count);
            Assert.Equal(database.Model.Relationships.Count, deserializedDb.Model.Relationships.Count);
            Assert.Equal(database.Model.Perspectives.Count, deserializedDb.Model.Perspectives.Count);
            Assert.Equal(database.Model.Roles.Count, deserializedDb.Model.Roles.Count);

            // ── 10. Validate deserialized TOM model ──
            var deserializedValidation = validator.ValidateTomModel(deserializedDb, modelDef.Name);
            Assert.True(deserializedValidation.IsValid,
                $"Deserialized TOM validation failed: {deserializedValidation.FormatMessages()}");

            // ── 11. Verify round-trip fidelity per table ──
            foreach (var origTable in database.Model.Tables)
            {
                var deserTable = deserializedDb.Model.Tables.Find(origTable.Name);
                Assert.NotNull(deserTable);
                Assert.Equal(origTable.Columns.Count, deserTable.Columns.Count);
                Assert.Equal(origTable.Measures.Count, deserTable.Measures.Count);
                Assert.Equal(origTable.Hierarchies.Count, deserTable.Hierarchies.Count);

                // Column lineage tags should survive round-trip
                foreach (var origCol in origTable.Columns)
                {
                    var deserCol = deserTable.Columns.Find(origCol.Name);
                    Assert.NotNull(deserCol);
                    Assert.Equal(origCol.DataType, deserCol.DataType);
                    Assert.Equal(origCol.LineageTag, deserCol.LineageTag);
                }

                // Measure expressions and lineage tags
                foreach (var origMeasure in origTable.Measures)
                {
                    var deserMeasure = deserTable.Measures.Find(origMeasure.Name);
                    Assert.NotNull(deserMeasure);
                    Assert.Equal(origMeasure.Expression, deserMeasure.Expression);
                    Assert.Equal(origMeasure.LineageTag, deserMeasure.LineageTag);
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
    /// Test that environment overrides are applied correctly when building.
    /// </summary>
    [Fact]
    public void ExampleProject_WithEnvironment_ShouldOverrideExpressions()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null) return;

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath)) return;

        // Load model and tables
        var registry = new TableRegistry(_serializer);
        registry.LoadTables(Path.Combine(exampleProjectPath, "tables"));

        var modelDef = _serializer.LoadFromFile<ModelDefinition>(
            Path.Combine(exampleProjectPath, "models", "sales_model.yaml"));

        // Load dev environment
        var envDef = _serializer.LoadFromFile<EnvironmentDefinition>(
            Path.Combine(exampleProjectPath, "environments", "dev.env.yml"));
        Assert.Equal("dev", envDef.Name);

        // Compose with environment
        var composer = new ModelComposer(registry);
        var database = composer.ComposeModel(
            modelDef, projectRootPath: exampleProjectPath, environment: envDef);

        // Verify environment overrides applied
        var dbNameExpr = database.Model.Expressions.Find("DatabaseName");
        Assert.NotNull(dbNameExpr);
        Assert.Contains("DevSalesDB", dbNameExpr.Expression);

        // Model should still be valid
        var validator = new Validator(_serializer);
        var validation = validator.ValidateTomModel(database, modelDef.Name);
        Assert.True(validation.IsValid, validation.FormatMessages());
    }

    /// <summary>
    /// Test that PBIP generation from the example project produces valid structure
    /// with TMDL that can be deserialized.
    /// </summary>
    [Fact]
    public void ExampleProject_PbipGeneration_ShouldProduceValidTmdl()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null) return;

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath)) return;

        var targetPath = Path.Combine(Path.GetTempPath(), $"pbt_example_pbip_{Guid.NewGuid()}");
        Directory.CreateDirectory(targetPath);

        try
        {
            var registry = new TableRegistry(_serializer);
            registry.LoadTables(Path.Combine(exampleProjectPath, "tables"));

            var modelDef = _serializer.LoadFromFile<ModelDefinition>(
                Path.Combine(exampleProjectPath, "models", "sales_model.yaml"));

            var composer = new ModelComposer(registry);
            var database = composer.ComposeModel(
                modelDef, projectRootPath: exampleProjectPath);

            // Generate PBIP
            PbipGenerator.GeneratePbipStructure(database, modelDef.Name, targetPath);

            // Verify PBIP structure
            var sanitizedName = FileNameSanitizer.Sanitize(modelDef.Name);
            Assert.True(File.Exists(Path.Combine(targetPath, $"{sanitizedName}.pbip")));
            Assert.True(Directory.Exists(Path.Combine(targetPath, $"{sanitizedName}.SemanticModel")));
            Assert.True(Directory.Exists(Path.Combine(targetPath, $"{sanitizedName}.Report")));

            // Round-trip validate the TMDL inside the PBIP
            var definitionDir = Path.Combine(targetPath, $"{sanitizedName}.SemanticModel", "definition");
            Assert.True(Directory.Exists(definitionDir));

            var deserializedDb = TmdlSerializer.DeserializeDatabaseFromFolder(definitionDir);
            Assert.Equal(database.Model.Tables.Count, deserializedDb.Model.Tables.Count);

            var validator = new Validator(_serializer);
            var validation = validator.ValidateTomModel(deserializedDb, modelDef.Name);
            Assert.True(validation.IsValid, $"PBIP TMDL validation failed: {validation.FormatMessages()}");
        }
        finally
        {
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, true);
        }
    }

    /// <summary>
    /// Test lineage tag stability: build twice, all tags should be reused on second build.
    /// </summary>
    [Fact]
    public void ExampleProject_LineageStability_ShouldReuseTagsOnRebuild()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null) return;

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath)) return;

        // Use a temp copy so we can write lineage without affecting the real project
        var tempProject = Path.Combine(Path.GetTempPath(), $"pbt_lineage_stability_{Guid.NewGuid()}");
        CopyDirectory(exampleProjectPath, tempProject);

        // Remove existing lineage manifest so first build generates fresh tags
        var existingLineage = Path.Combine(tempProject, ".pbt", "lineage.yaml");
        if (File.Exists(existingLineage))
            File.Delete(existingLineage);

        try
        {
            var registry = new TableRegistry(_serializer);
            registry.LoadTables(Path.Combine(tempProject, "tables"));
            var modelDef = _serializer.LoadFromFile<ModelDefinition>(
                Path.Combine(tempProject, "models", "sales_model.yaml"));

            // First build
            var lineageService1 = new LineageManifestService(_serializer);
            lineageService1.LoadManifest(tempProject);
            var composer = new ModelComposer(registry);
            var db1 = composer.ComposeModel(
                modelDef, lineageService1, tempProject);
            lineageService1.SaveManifest(tempProject);

            Assert.True(lineageService1.NewTagCount > 0, "First build should generate new tags");

            // Second build with saved manifest
            var lineageService2 = new LineageManifestService(_serializer);
            lineageService2.LoadManifest(tempProject);
            var db2 = composer.ComposeModel(
                modelDef, lineageService2, tempProject);

            Assert.Equal(0, lineageService2.NewTagCount);
            Assert.True(lineageService2.ExistingTagCount > 0, "Second build should reuse all tags");

            // Verify tags match between builds
            foreach (var table1 in db1.Model.Tables)
            {
                var table2 = db2.Model.Tables.Find(table1.Name);
                Assert.NotNull(table2);
                Assert.Equal(table1.LineageTag, table2.LineageTag);

                foreach (var col1 in table1.Columns)
                {
                    var col2 = table2.Columns.Find(col1.Name);
                    Assert.NotNull(col2);
                    Assert.Equal(col1.LineageTag, col2.LineageTag);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempProject))
                Directory.Delete(tempProject, true);
        }
    }

    #region Helpers

    private static void AssertAllLineageTags(Model model)
    {
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

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    private string? FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "pbicomposer.sln")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        return null;
    }

    #endregion
}
