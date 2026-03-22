using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class TmdlSerializationTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void SerializeToTmdl_ExampleProject_ShouldCreateTmdlFiles()
    {
        // Arrange
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            return; // Skip if not in project directory
        }

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath))
        {
            return; // Skip if example doesn't exist
        }

        // Create temp output directory
        var outputPath = Path.Combine(Path.GetTempPath(), $"tmdl_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(outputPath);

        try
        {
            // Load table registry
            var tablesPath = Path.Combine(exampleProjectPath, "tables");
            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            // Load model definition
            var modelPath = Path.Combine(exampleProjectPath, "models", "sales_model.yaml");
            var modelDef = _serializer.LoadFromFile<ModelDefinition>(modelPath);

            // Compose model
            var composer = new ModelComposer(registry);
            var database = composer.ComposeModel(modelDef, 1600);

            // Act - Serialize to TMDL
            TmdlSerializer.SerializeDatabaseToFolder(database, outputPath);

            // Assert - Files were created
            Assert.True(Directory.Exists(outputPath), "Output directory should exist");
            Assert.True(File.Exists(Path.Combine(outputPath, "database.tmdl")), "database.tmdl should exist");
            Assert.True(File.Exists(Path.Combine(outputPath, "model.tmdl")), "model.tmdl should exist");

            var tablesDir = Path.Combine(outputPath, "tables");
            Assert.True(Directory.Exists(tablesDir), "tables directory should exist");
            Assert.True(File.Exists(Path.Combine(tablesDir, "Sales.tmdl")), "Sales.tmdl should exist");
            Assert.True(File.Exists(Path.Combine(tablesDir, "Customers.tmdl")), "Customers.tmdl should exist");

            // Assert - Database TMDL content
            var databaseTmdl = File.ReadAllText(Path.Combine(outputPath, "database.tmdl"));
            Assert.Contains("database SalesAnalytics", databaseTmdl);
            Assert.Contains("compatibilityLevel: 1600", databaseTmdl);

            // Assert - Model TMDL content
            var modelTmdl = File.ReadAllText(Path.Combine(outputPath, "model.tmdl"));
            Assert.Contains($"model {database.Model.Name}", modelTmdl);

            // Assert - Sales table TMDL content
            var salesTmdl = File.ReadAllText(Path.Combine(tablesDir, "Sales.tmdl"));
            Assert.Contains("table Sales", salesTmdl);
            Assert.Contains("measure 'Total Sales'", salesTmdl);
            Assert.Contains("SUM(Sales[Amount])", salesTmdl);
            Assert.Contains("column OrderID", salesTmdl);
            Assert.Contains("column OrderDate", salesTmdl);
            Assert.Contains("column Amount", salesTmdl);
            Assert.Contains("column CustomerID", salesTmdl);

            // Assert - Customers table TMDL content
            var customersTmdl = File.ReadAllText(Path.Combine(tablesDir, "Customers.tmdl"));
            Assert.Contains("table Customers", customersTmdl);
            Assert.Contains("column CustomerID", customersTmdl);
            Assert.Contains("column CustomerName", customersTmdl);

            // Assert - Relationships exist (they are in the model, verified by deserialize test)
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public void DeserializeFromTmdl_RoundTrip_ShouldMatchOriginal()
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

        var outputPath = Path.Combine(Path.GetTempPath(), $"tmdl_roundtrip_{Guid.NewGuid()}");
        Directory.CreateDirectory(outputPath);

        try
        {
            // Load and compose original model
            var tablesPath = Path.Combine(exampleProjectPath, "tables");
            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            var modelPath = Path.Combine(exampleProjectPath, "models", "sales_model.yaml");
            var modelDef = _serializer.LoadFromFile<ModelDefinition>(modelPath);

            var composer = new ModelComposer(registry);
            var originalDatabase = composer.ComposeModel(modelDef, 1600);

            // Serialize to TMDL
            TmdlSerializer.SerializeDatabaseToFolder(originalDatabase, outputPath);

            // Act - Deserialize back from TMDL
            var deserializedDatabase = TmdlSerializer.DeserializeDatabaseFromFolder(outputPath);

            // Assert - Basic structure matches
            Assert.Equal(originalDatabase.Name, deserializedDatabase.Name);
            Assert.Equal(originalDatabase.CompatibilityLevel, deserializedDatabase.CompatibilityLevel);
            Assert.Equal(originalDatabase.Model.Tables.Count, deserializedDatabase.Model.Tables.Count);
            Assert.Equal(originalDatabase.Model.Relationships.Count, deserializedDatabase.Model.Relationships.Count);

            // Assert - Tables match
            foreach (var originalTable in originalDatabase.Model.Tables)
            {
                var deserializedTable = deserializedDatabase.Model.Tables.Find(originalTable.Name);
                Assert.NotNull(deserializedTable);
                Assert.Equal(originalTable.Columns.Count, deserializedTable.Columns.Count);
                Assert.Equal(originalTable.Measures.Count, deserializedTable.Measures.Count);
            }
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
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
