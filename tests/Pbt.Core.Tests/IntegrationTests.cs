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
        var projectYaml = Path.Combine(exampleProjectPath, "project.yml");

        // Act - Load project configuration
        var project = _serializer.LoadFromFile<ProjectDefinition>(projectYaml);

        // Act - Load table registry
        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act - Load model
        var salesModelPath = Path.Combine(modelsPath, "sales_model.yaml");
        var model = _serializer.LoadFromFile<ModelDefinition>(salesModelPath);

        // Assert - Project
        Assert.Equal("SampleProject", project.Name);
        Assert.Equal(1600, project.CompatibilityLevel);

        // Assert - Tables loaded
        Assert.Equal(2, registry.Count);
        Assert.True(registry.ContainsTable("Sales"));
        Assert.True(registry.ContainsTable("Customers"));

        // Assert - Sales table details
        var salesTable = registry.GetTable("Sales");
        Assert.Equal("Sales", salesTable.Name);
        Assert.NotNull(salesTable.MExpression);
        Assert.Contains("OrderID", salesTable.Columns.Select(c => c.Name));
        Assert.Contains("OrderDate", salesTable.Columns.Select(c => c.Name));
        Assert.Contains("Amount", salesTable.Columns.Select(c => c.Name));
        Assert.Contains("CustomerID", salesTable.Columns.Select(c => c.Name));

        // Assert - Customers table details
        var customersTable = registry.GetTable("Customers");
        Assert.Equal("Customers", customersTable.Name);
        Assert.Contains("CustomerID", customersTable.Columns.Select(c => c.Name));
        Assert.Contains("CustomerName", customersTable.Columns.Select(c => c.Name));

        // Assert - Model loaded
        Assert.Equal("SalesAnalytics", model.Name);
        Assert.Equal(2, model.Tables.Count);
        Assert.Contains(model.Tables, t => t.Ref == "Sales");
        Assert.Contains(model.Tables, t => t.Ref == "Customers");

        // Assert - Relationships
        Assert.Single(model.Relationships);
        var relationship = model.Relationships[0];
        Assert.Equal("Sales", relationship.FromTable);
        Assert.Equal("CustomerID", relationship.FromColumn);
        Assert.Equal("Customers", relationship.ToTable);
        Assert.Equal("CustomerID", relationship.ToColumn);
        Assert.Equal("ManyToOne", relationship.Cardinality);

        // Assert - Measures
        Assert.Equal(3, model.Measures.Count);
        Assert.Contains(model.Measures, m => m.Name == "Total Sales");
        Assert.Contains(model.Measures, m => m.Name == "Number of Orders");
        Assert.Contains(model.Measures, m => m.Name == "Average Order Value");

        var totalSalesMeasure = model.Measures.First(m => m.Name == "Total Sales");
        Assert.Equal("Sales", totalSalesMeasure.Table);
        Assert.Equal("SUM(Sales[Amount])", totalSalesMeasure.Expression);
        Assert.Equal("$#,##0.00", totalSalesMeasure.FormatString);
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
