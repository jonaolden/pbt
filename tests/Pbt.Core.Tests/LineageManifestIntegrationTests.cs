using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class LineageManifestIntegrationTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void BuildModel_WithLineageService_ShouldPreserveTagsAcrossBuilds()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"lineage_integration_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var tablesPath = Path.Combine(tempPath, "tables");
            Directory.CreateDirectory(tablesPath);

            // Create a simple table
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" }
                }
            };
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "sales.yaml"));

            // Create a model
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } },
                Measures = new List<MeasureDefinition>
                {
                    new() { Name = "Total Sales", Table = "Sales", Expression = "SUM(Sales[Amount])" }
                }
            };

            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            var composer = new ModelComposer(registry);

            // First build - generate tags
            var lineageService1 = new LineageManifestService(_serializer);
            lineageService1.LoadManifest(tempPath);

            var database1 = composer.ComposeModel(modelDef, lineageService1);

            var salesTable1 = database1.Model.Tables.Find("Sales");
            var tableTag1 = salesTable1!.LineageTag;
            var columnTag1 = salesTable1.Columns.Find("Amount")!.LineageTag;
            var measureTag1 = salesTable1.Measures.Find("Total Sales")!.LineageTag;

            lineageService1.SaveManifest(tempPath);

            Assert.Equal(3, lineageService1.NewTagCount); // 3 new tags generated
            Assert.Equal(0, lineageService1.ExistingTagCount);

            // Second build - preserve tags
            var lineageService2 = new LineageManifestService(_serializer);
            lineageService2.LoadManifest(tempPath);

            var database2 = composer.ComposeModel(modelDef, lineageService2);

            var salesTable2 = database2.Model.Tables.Find("Sales");
            var tableTag2 = salesTable2!.LineageTag;
            var columnTag2 = salesTable2.Columns.Find("Amount")!.LineageTag;
            var measureTag2 = salesTable2.Measures.Find("Total Sales")!.LineageTag;

            // Assert - Tags should be preserved
            Assert.Equal(tableTag1, tableTag2);
            Assert.Equal(columnTag1, columnTag2);
            Assert.Equal(measureTag1, measureTag2);

            Assert.Equal(0, lineageService2.NewTagCount); // No new tags
            Assert.Equal(3, lineageService2.ExistingTagCount); // 3 existing tags
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void BuildModel_AddNewColumn_ShouldGenerateOnlyNewTag()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"lineage_integration_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var tablesPath = Path.Combine(tempPath, "tables");
            Directory.CreateDirectory(tablesPath);

            // First build - one column
            var tableDef1 = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" }
                }
            };
            _serializer.SaveToFile(tableDef1, Path.Combine(tablesPath, "sales.yaml"));

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } }
            };

            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            var composer = new ModelComposer(registry);

            var lineageService1 = new LineageManifestService(_serializer);
            lineageService1.LoadManifest(tempPath);

            composer.ComposeModel(modelDef, lineageService1);
            lineageService1.SaveManifest(tempPath);

            var initialNewCount = lineageService1.NewTagCount;
            Assert.Equal(2, initialNewCount); // Table + Amount column

            // Second build - add new column
            var tableDef2 = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" },
                    new() { Name = "Quantity", Type = "Int64" } // New column
                }
            };
            _serializer.SaveToFile(tableDef2, Path.Combine(tablesPath, "sales.yaml"));

            registry.Clear();
            registry.LoadTables(tablesPath);

            var lineageService2 = new LineageManifestService(_serializer);
            lineageService2.LoadManifest(tempPath);

            composer.ComposeModel(modelDef, lineageService2);

            // Assert
            Assert.Equal(1, lineageService2.NewTagCount); // Only Quantity column is new
            Assert.Equal(2, lineageService2.ExistingTagCount); // Table + Amount are existing
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void BuildModel_WithExplicitLineageTag_ShouldUseProvidedTag()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"lineage_integration_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var tablesPath = Path.Combine(tempPath, "tables");
            Directory.CreateDirectory(tablesPath);

            var explicitTag = "12345678-1234-1234-1234-123456789012";

            var tableDef = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table(...) in Source",
                LineageTag = explicitTag, // Explicit tag provided
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" }
                }
            };
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "sales.yaml"));

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } }
            };

            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            var composer = new ModelComposer(registry);
            var lineageService = new LineageManifestService(_serializer);
            lineageService.LoadManifest(tempPath);

            // Act
            var database = composer.ComposeModel(modelDef, lineageService);

            // Assert
            var salesTable = database.Model.Tables.Find("Sales");
            Assert.Equal(explicitTag, salesTable!.LineageTag);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }
}
