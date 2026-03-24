using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class YamlSerializerTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void LoadModelDefinition_ShouldDeserializeProjectLevelConfig()
    {
        // Arrange — model.yaml now carries project-level configuration
        var yaml = @"
name: SalesAnalytics
description: Example sales model
compatibility_level: 1600
";

        // Act
        var model = _serializer.Deserialize<ModelDefinition>(yaml);

        // Assert
        Assert.Equal("SalesAnalytics", model.Name);
        Assert.Equal("Example sales model", model.Description);
        Assert.Equal(1600, model.CompatibilityLevel);
    }

    [Fact]
    public void LoadTableDefinition_ShouldDeserialize()
    {
        // Arrange
        var yaml = @"
name: Sales
description: Sales fact table
m_expression: |
  let
    Source = #table(...)
  in
    Source

columns:
  - name: OrderID
    type: Int64
    source_column: OrderID
    description: Unique order identifier

  - name: Amount
    type: Decimal
    format_string: ""$#,##0.00""
";

        // Act
        var table = _serializer.Deserialize<TableDefinition>(yaml);

        // Assert
        Assert.Equal("Sales", table.Name);
        Assert.Equal("Sales fact table", table.Description);
        Assert.Contains("let", table.MExpression);
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("OrderID", table.Columns[0].Name);
        Assert.Equal("Int64", table.Columns[0].Type);
        Assert.Equal("OrderID", table.Columns[0].SourceColumn);
        Assert.Equal("Amount", table.Columns[1].Name);
        Assert.Equal("$#,##0.00", table.Columns[1].FormatString);
    }

    [Fact]
    public void LoadModelDefinition_ShouldDeserialize()
    {
        // Arrange
        var yaml = @"
name: SalesAnalytics
description: Sales analytics model

tables:
  - ref: Sales
  - ref: Customers

relationships:
  - from_table: Sales
    from_column: CustomerID
    to_table: Customers
    to_column: CustomerID
    cardinality: ManyToOne
    cross_filter_direction: Both
    active: true

measures:
  - name: Total Sales
    table: Sales
    expression: SUM(Sales[Amount])
    format_string: ""$#,##0.00""
    display_folder: Sales Metrics
";

        // Act
        var model = _serializer.Deserialize<ModelDefinition>(yaml);

        // Assert
        Assert.Equal("SalesAnalytics", model.Name);
        Assert.Equal("Sales analytics model", model.Description);
        Assert.Equal(2, model.Tables.Count);
        Assert.Equal("Sales", model.Tables[0].Ref);
        Assert.Equal("Customers", model.Tables[1].Ref);

        Assert.Single(model.Relationships);
        Assert.Equal("Sales", model.Relationships[0].FromTable);
        Assert.Equal("CustomerID", model.Relationships[0].FromColumn);
        Assert.Equal("Customers", model.Relationships[0].ToTable);
        Assert.Equal("ManyToOne", model.Relationships[0].Cardinality);
        Assert.Equal("Both", model.Relationships[0].CrossFilterDirection);
        Assert.True(model.Relationships[0].Active);

        Assert.Single(model.Measures);
        Assert.Equal("Total Sales", model.Measures[0].Name);
        Assert.Equal("Sales", model.Measures[0].Table);
        Assert.Equal("SUM(Sales[Amount])", model.Measures[0].Expression);
        Assert.Equal("Sales Metrics", model.Measures[0].DisplayFolder);
    }

    [Fact]
    public void SerializeAndDeserialize_ShouldRoundTrip()
    {
        // Arrange
        var original = new TableDefinition
        {
            Name = "TestTable",
            Description = "Test description",
            MExpression = "let Source = ... in Source",
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "ID", Type = "Int64", SourceColumn = "id" },
                new() { Name = "Name", Type = "String", FormatString = null }
            }
        };

        // Act
        var yaml = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<TableDefinition>(yaml);

        // Assert
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.MExpression, deserialized.MExpression);
        Assert.Equal(original.Columns.Count, deserialized.Columns.Count);
        Assert.Equal(original.Columns[0].Name, deserialized.Columns[0].Name);
        Assert.Equal(original.Columns[0].SourceColumn, deserialized.Columns[0].SourceColumn);
    }

    [Fact]
    public void LoadTableDefinition_WithCalculatedColumn_ShouldDeserialize()
    {
        // Arrange
        var yaml = @"
name: Date
description: Date dimension table
columns:
  - name: Date
    type: DateTime
    source_column: DATE_FMT
    description: Calendar date

  - name: Last and Next 12 Months
    type: Boolean
    expression: 'IF( [Month Fmt] >= DATEADD( MONTH, -12, TODAY() ) && [Month Fmt] <= DATEADD( MONTH, 12, TODAY() ), TRUE(), FALSE() )'
    description: Flag for dates within 12 months range
    format_string: '""TRUE"";""TRUE"";""FALSE""'
    is_hidden: true
    display_folder: Sets
";

        // Act
        var table = _serializer.Deserialize<TableDefinition>(yaml);

        // Assert
        Assert.Equal("Date", table.Name);
        Assert.Equal(2, table.Columns.Count);

        // Verify data column
        var dateColumn = table.Columns[0];
        Assert.Equal("Date", dateColumn.Name);
        Assert.Equal("DateTime", dateColumn.Type);
        Assert.Equal("DATE_FMT", dateColumn.SourceColumn);
        Assert.Null(dateColumn.Expression);

        // Verify calculated column
        var calcColumn = table.Columns[1];
        Assert.Equal("Last and Next 12 Months", calcColumn.Name);
        Assert.Equal("Boolean", calcColumn.Type);
        Assert.Null(calcColumn.SourceColumn);
        Assert.NotNull(calcColumn.Expression);
        Assert.Contains("DATEADD", calcColumn.Expression);
        Assert.Equal(true, calcColumn.IsHidden);
        Assert.Equal("Sets", calcColumn.DisplayFolder);
        Assert.Equal("\"TRUE\";\"TRUE\";\"FALSE\"", calcColumn.FormatString);
    }

    [Fact]
    public void SerializeAndDeserialize_WithCalculatedColumn_ShouldRoundTrip()
    {
        // Arrange
        var original = new TableDefinition
        {
            Name = "TestTable",
            Description = "Test description",
            MExpression = "let Source = ... in Source",
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "ID", Type = "Int64", SourceColumn = "id" },
                new()
                {
                    Name = "Calculated Column",
                    Type = "Decimal",
                    Expression = "DIVIDE([Amount], [Quantity], 0)",
                    Description = "Unit price calculation",
                    FormatString = "$#,##0.00",
                    IsHidden = true,
                    DisplayFolder = "Calculations"
                }
            }
        };

        // Act
        var yaml = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<TableDefinition>(yaml);

        // Assert
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Columns.Count, deserialized.Columns.Count);

        // Verify data column round-trip
        Assert.Equal(original.Columns[0].Name, deserialized.Columns[0].Name);
        Assert.Equal(original.Columns[0].SourceColumn, deserialized.Columns[0].SourceColumn);
        Assert.Null(deserialized.Columns[0].Expression);

        // Verify calculated column round-trip
        var calcColumn = deserialized.Columns[1];
        Assert.Equal("Calculated Column", calcColumn.Name);
        Assert.Equal("Decimal", calcColumn.Type);
        Assert.Null(calcColumn.SourceColumn);
        Assert.Equal("DIVIDE([Amount], [Quantity], 0)", calcColumn.Expression);
        Assert.Equal("Unit price calculation", calcColumn.Description);
        Assert.Equal("$#,##0.00", calcColumn.FormatString);
        Assert.True(calcColumn.IsHidden);
        Assert.Equal("Calculations", calcColumn.DisplayFolder);
    }

    [Fact]
    public void LoadFromFile_ExampleProject_ShouldDeserialize()
    {
        // Arrange
        var projectRoot = FindProjectRoot();
        var salesTablePath = Path.Combine(projectRoot, "examples/sample_project/tables/sales.yaml");

        // Skip if running in environment without example files
        if (!File.Exists(salesTablePath))
        {
            return;
        }

        // Act
        var table = _serializer.LoadFromFile<TableDefinition>(salesTablePath);

        // Assert
        Assert.Equal("Sales", table.Name);
        Assert.NotEmpty(table.Columns);
        Assert.Contains(table.Columns, c => c.Name == "OrderID");
    }

    [Fact]
    public void DeserializeModel_WithRelationshipShorthand_ShouldParse()
    {
        // Arrange
        var yaml = @"
name: TestModel
tables:
  - ref: Sales
  - ref: Customers
relationships:
  - ""Sales.CustomerID -> Customers.CustomerID""
";

        // Act
        var model = _serializer.Deserialize<ModelDefinition>(yaml);

        // Assert
        Assert.Single(model.Relationships);
        var rel = model.Relationships[0];
        Assert.Equal("Sales", rel.FromTable);
        Assert.Equal("CustomerID", rel.FromColumn);
        Assert.Equal("Customers", rel.ToTable);
        Assert.Equal("CustomerID", rel.ToColumn);
        Assert.Equal("ManyToOne", rel.Cardinality);
        Assert.Equal("Single", rel.CrossFilterDirection);
        Assert.True(rel.Active);
    }

    [Fact]
    public void DeserializeModel_WithVerboseRelationship_ShouldParse()
    {
        // Arrange
        var yaml = @"
name: TestModel
tables:
  - ref: Sales
  - ref: Customers
relationships:
  - from_table: Sales
    from_column: CustomerID
    to_table: Customers
    to_column: CustomerID
    cardinality: OneToMany
    cross_filter_direction: Both
    active: false
    rely_on_referential_integrity: true
";

        // Act
        var model = _serializer.Deserialize<ModelDefinition>(yaml);

        // Assert
        Assert.Single(model.Relationships);
        var rel = model.Relationships[0];
        Assert.Equal("Sales", rel.FromTable);
        Assert.Equal("CustomerID", rel.FromColumn);
        Assert.Equal("Customers", rel.ToTable);
        Assert.Equal("CustomerID", rel.ToColumn);
        Assert.Equal("OneToMany", rel.Cardinality);
        Assert.Equal("Both", rel.CrossFilterDirection);
        Assert.False(rel.Active);
        Assert.True(rel.RelyOnReferentialIntegrity);
    }

    [Fact]
    public void RelationshipShorthandParser_ValidShorthand_ShouldParse()
    {
        // Act
        var result = RelationshipShorthandParser.TryParseShorthand("Sales.CustomerID -> Customers.CustomerID");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Sales", result.FromTable);
        Assert.Equal("CustomerID", result.FromColumn);
        Assert.Equal("Customers", result.ToTable);
        Assert.Equal("CustomerID", result.ToColumn);
        Assert.Equal("ManyToOne", result.Cardinality);
        Assert.Equal("Single", result.CrossFilterDirection);
        Assert.True(result.Active);
    }

    [Fact]
    public void RelationshipShorthandParser_InvalidString_ShouldReturnNull()
    {
        // Act
        var result = RelationshipShorthandParser.TryParseShorthand("not a valid shorthand");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void RelationshipShorthandParser_IsShorthand_ShouldDetectArrow()
    {
        Assert.True(RelationshipShorthandParser.IsShorthand("Sales.ID -> Customers.ID"));
        Assert.False(RelationshipShorthandParser.IsShorthand("just a plain string"));
    }

    [Fact]
    public void DeserializeTable_WithNewColumnProperties_ShouldParse()
    {
        // Arrange
        var yaml = @"
name: Products
columns:
  - name: ProductID
    type: Int64
    is_key: true
    data_category: Barcode
    summarize_by: None
  - name: Price
    type: Decimal
    summarize_by: Sum
    sort_by_column: ProductID
    annotations:
      TestAnnotation: TestValue
";

        // Act
        var table = _serializer.Deserialize<TableDefinition>(yaml);

        // Assert
        Assert.Equal(2, table.Columns.Count);

        var idCol = table.Columns[0];
        Assert.True(idCol.IsKey);
        Assert.Equal("Barcode", idCol.DataCategory);
        Assert.Equal("None", idCol.SummarizeBy);

        var priceCol = table.Columns[1];
        Assert.Equal("Sum", priceCol.SummarizeBy);
        Assert.Equal("ProductID", priceCol.SortByColumn);
        Assert.NotNull(priceCol.Annotations);
        Assert.Equal("TestValue", priceCol.Annotations["TestAnnotation"]);
    }

    [Fact]
    public void DeserializeTable_WithPartitions_ShouldParse()
    {
        // Arrange
        var yaml = @"
name: Sales
columns:
  - name: Amount
    type: Decimal
partitions:
  - name: Historical
    mode: Import
    m_expression: |
      let Source = #table(...) in Source
  - name: Current
    mode: DirectQuery
    m_expression: |
      let Source = #table(...) in Source
";

        // Act
        var table = _serializer.Deserialize<TableDefinition>(yaml);

        // Assert
        Assert.NotNull(table.Partitions);
        Assert.Equal(2, table.Partitions.Count);
        Assert.Equal("Historical", table.Partitions[0].Name);
        Assert.Equal("Import", table.Partitions[0].Mode);
        Assert.Equal("Current", table.Partitions[1].Name);
        Assert.Equal("DirectQuery", table.Partitions[1].Mode);
    }

    private string FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null && !File.Exists(Path.Combine(directory, "pbicomposer.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }
        return directory ?? Directory.GetCurrentDirectory();
    }
}
