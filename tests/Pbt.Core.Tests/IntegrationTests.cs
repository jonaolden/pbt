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
