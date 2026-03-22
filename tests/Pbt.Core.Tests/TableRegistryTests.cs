using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class TableRegistryTests
{
    private readonly YamlSerializer _serializer = new();
    private readonly string _testDataPath;

    public TableRegistryTests()
    {
        // Create temp directory for test data
        _testDataPath = Path.Combine(Path.GetTempPath(), $"pbicomposer_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataPath);
    }

    [Fact]
    public void LoadTables_ValidDirectory_ShouldLoadAllTables()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);

        CreateTestTable(tablesPath, "sales.yaml", "Sales");
        CreateTestTable(tablesPath, "customers.yaml", "Customers");
        CreateTestTable(tablesPath, "products.yaml", "Products");

        var registry = new TableRegistry(_serializer);

        // Act
        registry.LoadTables(tablesPath);

        // Assert
        Assert.Equal(3, registry.Count);
        Assert.True(registry.ContainsTable("Sales"));
        Assert.True(registry.ContainsTable("Customers"));
        Assert.True(registry.ContainsTable("Products"));
    }

    [Fact]
    public void LoadTables_CaseInsensitive_ShouldFindTable()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act & Assert
        Assert.True(registry.ContainsTable("sales"));
        Assert.True(registry.ContainsTable("SALES"));
        Assert.True(registry.ContainsTable("Sales"));
    }

    [Fact]
    public void LoadTables_DuplicateNames_ShouldThrowException()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);

        CreateTestTable(tablesPath, "sales1.yaml", "Sales");
        CreateTestTable(tablesPath, "sales2.yaml", "Sales"); // Duplicate name

        var registry = new TableRegistry(_serializer);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => registry.LoadTables(tablesPath));
        Assert.Contains("Duplicate table names", exception.Message);
        Assert.Contains("Sales", exception.Message);
    }

    [Fact]
    public void LoadTables_DirectoryNotFound_ShouldThrowException()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var nonExistentPath = Path.Combine(_testDataPath, "nonexistent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => registry.LoadTables(nonExistentPath));
    }

    [Fact]
    public void LoadTables_EmptyDirectory_ShouldThrowException()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "empty_tables");
        Directory.CreateDirectory(tablesPath);

        var registry = new TableRegistry(_serializer);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => registry.LoadTables(tablesPath));
        Assert.Contains("No YAML files found", exception.Message);
    }

    [Fact]
    public void GetTable_ExistingTable_ShouldReturnTable()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act
        var table = registry.GetTable("Sales");

        // Assert
        Assert.NotNull(table);
        Assert.Equal("Sales", table.Name);
        Assert.NotNull(table.SourceFilePath);
        Assert.Contains("sales.yaml", table.SourceFilePath);
    }

    [Fact]
    public void GetTable_NonExistentTable_ShouldThrowException()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(() => registry.GetTable("NonExistent"));
        Assert.Contains("NonExistent", exception.Message);
        Assert.Contains("Available tables", exception.Message);
    }

    [Fact]
    public void TryGetTable_ExistingTable_ShouldReturnTrue()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act
        var found = registry.TryGetTable("Sales", out var table);

        // Assert
        Assert.True(found);
        Assert.NotNull(table);
        Assert.Equal("Sales", table.Name);
    }

    [Fact]
    public void TryGetTable_NonExistentTable_ShouldReturnFalse()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act
        var found = registry.TryGetTable("NonExistent", out var table);

        // Assert
        Assert.False(found);
        Assert.Null(table);
    }

    [Fact]
    public void ListTables_ShouldReturnAllTableNames()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");
        CreateTestTable(tablesPath, "customers.yaml", "Customers");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act
        var tableNames = registry.ListTables();

        // Assert
        Assert.Equal(2, tableNames.Count);
        Assert.Contains("Sales", tableNames);
        Assert.Contains("Customers", tableNames);
    }

    [Fact]
    public void GetAllTables_ShouldReturnAllDefinitions()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");
        CreateTestTable(tablesPath, "customers.yaml", "Customers");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Act
        var tables = registry.GetAllTables().ToList();

        // Assert
        Assert.Equal(2, tables.Count);
        Assert.Contains(tables, t => t.Name == "Sales");
        Assert.Contains(tables, t => t.Name == "Customers");
    }

    [Fact]
    public void LoadTables_WithBothYamlAndYmlExtensions_ShouldLoadBoth()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");
        CreateTestTable(tablesPath, "customers.yml", "Customers"); // .yml extension

        var registry = new TableRegistry(_serializer);

        // Act
        registry.LoadTables(tablesPath);

        // Assert
        Assert.Equal(2, registry.Count);
        Assert.True(registry.ContainsTable("Sales"));
        Assert.True(registry.ContainsTable("Customers"));
    }

    [Fact]
    public void Clear_ShouldRemoveAllTables()
    {
        // Arrange
        var tablesPath = Path.Combine(_testDataPath, "tables");
        Directory.CreateDirectory(tablesPath);
        CreateTestTable(tablesPath, "sales.yaml", "Sales");

        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);
        Assert.Equal(1, registry.Count);

        // Act
        registry.Clear();

        // Assert
        Assert.Equal(0, registry.Count);
        Assert.False(registry.ContainsTable("Sales"));
    }

    private void CreateTestTable(string directory, string filename, string tableName)
    {
        var table = new TableDefinition
        {
            Name = tableName,
            Description = $"Test table {tableName}",
            MExpression = "let Source = #table(...) in Source",
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "ID", Type = "Int64" },
                new() { Name = "Name", Type = "String" }
            }
        };

        var filePath = Path.Combine(directory, filename);
        _serializer.SaveToFile(table, filePath);
    }
}
