using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class ModelComposerTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void ComposeModel_WithExampleProject_ShouldCreateValidDatabase()
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

        // Load table registry
        var tablesPath = Path.Combine(exampleProjectPath, "tables");
        var registry = new TableRegistry(_serializer);
        registry.LoadTables(tablesPath);

        // Load model definition
        var modelPath = Path.Combine(exampleProjectPath, "models", "sales_model.yaml");
        var modelDef = _serializer.LoadFromFile<ModelDefinition>(modelPath);

        // Act
        var composer = new ModelComposer(registry);
        var database = composer.ComposeModel(modelDef, projectRootPath: exampleProjectPath);

        // Assert - Database
        Assert.NotNull(database);
        Assert.Equal("SalesAnalytics", database.Name);
        Assert.Equal(1700, database.CompatibilityLevel);
        Assert.NotNull(database.Model);

        // Assert - Tables: 3 regular + 1 calc group + 1 field parameter = 5
        Assert.Equal(5, database.Model.Tables.Count);
        Assert.NotNull(database.Model.Tables.Find("Sales"));
        Assert.NotNull(database.Model.Tables.Find("Customers"));
        Assert.NotNull(database.Model.Tables.Find("DateDim"));
        Assert.NotNull(database.Model.Tables.Find("Time Intelligence"));
        Assert.NotNull(database.Model.Tables.Find("Sales Metric"));

        // Assert - Sales table
        var salesTable = database.Model.Tables.Find("Sales")!;
        Assert.Equal(6, salesTable.Columns.Count); // 5 data + 1 calculated
        Assert.NotNull(salesTable.Columns.Find("OrderID"));
        Assert.NotNull(salesTable.Columns.Find("IsLargeOrder"));
        Assert.IsType<CalculatedColumn>(salesTable.Columns.Find("IsLargeOrder"));

        // Assert - Sales has 2 partitions (Historical + Current)
        Assert.Equal(2, salesTable.Partitions.Count);
        Assert.Equal(ModeType.Import, salesTable.Partitions[0].Mode);

        // Assert - Column properties
        var orderIdCol = salesTable.Columns.Find("OrderID")!;
        Assert.True(orderIdCol.IsKey);
        Assert.Equal(AggregateFunction.None, orderIdCol.SummarizeBy);

        var amountCol = salesTable.Columns.Find("Amount")!;
        Assert.Equal(AggregateFunction.Sum, amountCol.SummarizeBy);
        Assert.Equal("$#,##0.00", amountCol.FormatString);

        // Assert - Customers table
        var customersTable = database.Model.Tables.Find("Customers")!;
        Assert.Equal(5, customersTable.Columns.Count);

        // Assert - Hierarchy
        Assert.Single(customersTable.Hierarchies);
        var geoHierarchy = customersTable.Hierarchies[0];
        Assert.Equal("Geography", geoHierarchy.Name);
        Assert.Equal(3, geoHierarchy.Levels.Count);

        // Assert - Data category and annotations
        Assert.Equal("City", customersTable.Columns.Find("City")!.DataCategory);
        Assert.Contains(customersTable.Columns.Find("Country")!.Annotations, a => a.Name == "PBI_GeoEncoding");

        // Assert - DateDim table (sort by column)
        var dateDimTable = database.Model.Tables.Find("DateDim")!;
        var monthNameCol = dateDimTable.Columns.Find("MonthName")!;
        var monthNumCol = dateDimTable.Columns.Find("MonthNum")!;
        Assert.Equal(monthNumCol, monthNameCol.SortByColumn);

        // Assert - Relationships
        Assert.Equal(2, database.Model.Relationships.Count);

        var custRelationship = database.Model.Relationships.Cast<SingleColumnRelationship>()
            .First(r => r.ToColumn.Table.Name == "Customers");
        Assert.Equal("Sales", custRelationship.FromColumn.Table.Name);
        Assert.Equal("CustomerID", custRelationship.FromColumn.Name);
        Assert.Equal(CrossFilteringBehavior.BothDirections, custRelationship.CrossFilteringBehavior);
        Assert.True(custRelationship.RelyOnReferentialIntegrity);
        Assert.True(custRelationship.IsActive);

        var dateRelationship = database.Model.Relationships.Cast<SingleColumnRelationship>()
            .First(r => r.ToColumn.Table.Name == "DateDim");
        Assert.Equal(CrossFilteringBehavior.OneDirection, dateRelationship.CrossFilteringBehavior);

        // Assert - Measures (table-level + model-level override)
        Assert.Equal(3, salesTable.Measures.Count);
        var totalSalesMeasure = salesTable.Measures.Find("Total Sales")!;
        Assert.Equal("SUM(Sales[Amount])", totalSalesMeasure.Expression);
        Assert.Equal("$#,##0.00", totalSalesMeasure.FormatString);
        Assert.Equal("Sales Metrics", totalSalesMeasure.DisplayFolder);
        Assert.NotNull(salesTable.Measures.Find("Average Order Value"));

        // Assert - Calculation group
        var calcGroupTable = database.Model.Tables.Find("Time Intelligence")!;
        Assert.NotNull(calcGroupTable.CalculationGroup);
        Assert.Equal(3, calcGroupTable.CalculationGroup.CalculationItems.Count);
        Assert.Equal(10, calcGroupTable.CalculationGroup.Precedence);

        // Assert - Perspectives
        Assert.Single(database.Model.Perspectives);
        Assert.Equal("Sales Overview", database.Model.Perspectives[0].Name);

        // Assert - Roles
        Assert.Single(database.Model.Roles);
        Assert.Equal("RegionManager", database.Model.Roles[0].Name);
        Assert.Equal(ModelPermission.Read, database.Model.Roles[0].ModelPermission);

        // Assert - Field parameter
        var fieldParamTable = database.Model.Tables.Find("Sales Metric")!;
        Assert.Contains(fieldParamTable.Annotations, a => a.Name == "ParameterMetadata");

        // Assert - Shared expressions (model-level)
        Assert.True(database.Model.Expressions.Count >= 1);

        // Assert - Lineage tags generated
        Assert.False(string.IsNullOrWhiteSpace(salesTable.LineageTag));
        Assert.False(string.IsNullOrWhiteSpace(orderIdCol.LineageTag));
        Assert.False(string.IsNullOrWhiteSpace(totalSalesMeasure.LineageTag));
    }

    [Fact]
    public void ComposeModel_InvalidTableReference_ShouldThrowException()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Create a valid table
            var tableDef = new TableDefinition
            {
                Name = "ValidTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };
            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "valid.yaml"));
            registry.LoadTables(tempPath);

            // Create model with invalid reference
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "NonExistentTable" }
                }
            };

            var composer = new ModelComposer(registry);

            // Act & Assert
            Assert.Throws<KeyNotFoundException>(() => composer.ComposeModel(modelDef));
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
    public void ComposeModel_InvalidRelationshipTable_ShouldThrowException()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Create table
            var tableDef = new TableDefinition
            {
                Name = "Table1",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };
            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "table1.yaml"));
            registry.LoadTables(tempPath);

            // Create model with invalid relationship
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "Table1" }
                },
                Relationships = new List<RelationshipDefinition>
                {
                    new()
                    {
                        FromTable = "Table1",
                        FromColumn = "ID",
                        ToTable = "NonExistentTable",
                        ToColumn = "ID"
                    }
                }
            };

            var composer = new ModelComposer(registry);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => composer.ComposeModel(modelDef));
            Assert.Contains("NonExistentTable", exception.Message);
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
    public void ComposeModel_InvalidMeasureTable_ShouldThrowException()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Create table
            var tableDef = new TableDefinition
            {
                Name = "Table1",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };
            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "table1.yaml"));
            registry.LoadTables(tempPath);

            // Create model with measure for non-existent table
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "Table1" }
                },
                Measures = new List<MeasureDefinition>
                {
                    new()
                    {
                        Name = "Test Measure",
                        Table = "NonExistentTable",
                        Expression = "1"
                    }
                }
            };

            var composer = new ModelComposer(registry);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => composer.ComposeModel(modelDef));
            Assert.Contains("NonExistentTable", exception.Message);
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
    public void BuildTable_WithHierarchy_ShouldCreateHierarchy()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "DateTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Year", Type = "Int64" },
                    new() { Name = "Month", Type = "Int64" },
                    new() { Name = "Day", Type = "Int64" }
                },
                Hierarchies = new List<HierarchyDefinition>
                {
                    new()
                    {
                        Name = "Date Hierarchy",
                        Levels = new List<LevelDefinition>
                        {
                            new() { Name = "Year", Column = "Year" },
                            new() { Name = "Month", Column = "Month" },
                            new() { Name = "Day", Column = "Day" }
                        }
                    }
                }
            };

            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "date.yaml"));
            registry.LoadTables(tempPath);

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "DateTable" } }
            };

            var composer = new ModelComposer(registry);

            // Act
            var database = composer.ComposeModel(modelDef);

            // Assert
            var table = database.Model.Tables.Find("DateTable");
            Assert.NotNull(table);
            Assert.Single(table.Hierarchies);

            var hierarchy = table.Hierarchies[0];
            Assert.Equal("Date Hierarchy", hierarchy.Name);
            Assert.Equal(3, hierarchy.Levels.Count);
            Assert.Equal("Year", hierarchy.Levels[0].Name);
            Assert.Equal("Month", hierarchy.Levels[1].Name);
            Assert.Equal("Day", hierarchy.Levels[2].Name);
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
    public void BuildTable_WithCalculatedColumn_ShouldCreateCalculatedColumn()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "DateTable",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    // Regular data column
                    new() { Name = "Date", Type = "DateTime", SourceColumn = "Date" },
                    // Calculated column with expression
                    new()
                    {
                        Name = "Last and Next 12 Months",
                        Type = "Boolean",
                        Expression = "IF( [Month Fmt] >= DATEADD( MONTH, -12, TODAY() ) && [Month Fmt] <= DATEADD( MONTH, 12, TODAY() ), TRUE(), FALSE() )",
                        Description = "Flag for dates within 12 months",
                        FormatString = "\"TRUE\";\"TRUE\";\"FALSE\"",
                        IsHidden = true,
                        DisplayFolder = "Sets"
                    }
                }
            };

            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "date.yaml"));
            registry.LoadTables(tempPath);

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "DateTable" } }
            };

            var composer = new ModelComposer(registry);

            // Act
            var database = composer.ComposeModel(modelDef);

            // Assert
            var table = database.Model.Tables.Find("DateTable");
            Assert.NotNull(table);
            Assert.Equal(2, table.Columns.Count);

            // Verify data column
            var dateColumn = table.Columns.Find("Date");
            Assert.NotNull(dateColumn);
            Assert.IsType<DataColumn>(dateColumn);
            Assert.Equal(DataType.DateTime, dateColumn.DataType);
            Assert.Equal("Date", ((DataColumn)dateColumn).SourceColumn);

            // Verify calculated column
            var calcColumn = table.Columns.Find("Last and Next 12 Months");
            Assert.NotNull(calcColumn);
            Assert.IsType<CalculatedColumn>(calcColumn);
            var calculatedColumn = (CalculatedColumn)calcColumn;
            Assert.Equal(DataType.Boolean, calculatedColumn.DataType);
            Assert.Contains("DATEADD", calculatedColumn.Expression);
            Assert.True(calculatedColumn.IsHidden);
            Assert.Equal("Sets", calculatedColumn.DisplayFolder);
            Assert.Equal("\"TRUE\";\"TRUE\";\"FALSE\"", calculatedColumn.FormatString);
            Assert.Equal("Flag for dates within 12 months", calculatedColumn.Description);
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
    public void BuildTable_WithMixedColumns_ShouldCreateCorrectColumnTypes()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    // Data columns with source_column
                    new() { Name = "Amount", Type = "Decimal", SourceColumn = "AMOUNT" },
                    new() { Name = "Quantity", Type = "Int64", SourceColumn = "QTY" },
                    // Calculated columns with expression
                    new()
                    {
                        Name = "Unit Price",
                        Type = "Decimal",
                        Expression = "DIVIDE([Amount], [Quantity], 0)"
                    },
                    new()
                    {
                        Name = "Is Large Order",
                        Type = "Boolean",
                        Expression = "IF([Amount] > 1000, TRUE(), FALSE())"
                    }
                }
            };

            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "sales.yaml"));
            registry.LoadTables(tempPath);

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } }
            };

            var composer = new ModelComposer(registry);

            // Act
            var database = composer.ComposeModel(modelDef);

            // Assert
            var table = database.Model.Tables.Find("Sales");
            Assert.NotNull(table);
            Assert.Equal(4, table.Columns.Count);

            // Verify data columns
            var amountColumn = table.Columns.Find("Amount") as DataColumn;
            Assert.NotNull(amountColumn);
            Assert.Equal("AMOUNT", amountColumn.SourceColumn);

            var quantityColumn = table.Columns.Find("Quantity") as DataColumn;
            Assert.NotNull(quantityColumn);
            Assert.Equal("QTY", quantityColumn.SourceColumn);

            // Verify calculated columns
            var unitPriceColumn = table.Columns.Find("Unit Price") as CalculatedColumn;
            Assert.NotNull(unitPriceColumn);
            Assert.Equal("DIVIDE([Amount], [Quantity], 0)", unitPriceColumn.Expression);

            var isLargeOrderColumn = table.Columns.Find("Is Large Order") as CalculatedColumn;
            Assert.NotNull(isLargeOrderColumn);
            Assert.Contains("IF([Amount] > 1000", isLargeOrderColumn.Expression);
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
    public void ComposeModel_TableLevelMeasures_ShouldBeIncluded()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" }
                },
                Measures = new List<MeasureDefinition>
                {
                    new()
                    {
                        Name = "Total Sales",
                        Table = "Sales",
                        Expression = "SUM(Sales[Amount])",
                        FormatString = "$#,##0.00",
                        DisplayFolder = "Metrics"
                    },
                    new()
                    {
                        Name = "Avg Sales",
                        Table = "Sales",
                        Expression = "AVERAGE(Sales[Amount])"
                    }
                }
            };

            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "sales.yaml"));
            registry.LoadTables(tempPath);

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } }
            };

            var composer = new ModelComposer(registry);

            // Act
            var database = composer.ComposeModel(modelDef);

            // Assert
            var table = database.Model.Tables.Find("Sales");
            Assert.NotNull(table);
            Assert.Equal(2, table.Measures.Count);

            var totalSales = table.Measures.Find("Total Sales");
            Assert.NotNull(totalSales);
            Assert.Equal("SUM(Sales[Amount])", totalSales.Expression);
            Assert.Equal("$#,##0.00", totalSales.FormatString);
            Assert.Equal("Metrics", totalSales.DisplayFolder);

            var avgSales = table.Measures.Find("Avg Sales");
            Assert.NotNull(avgSales);
            Assert.Equal("AVERAGE(Sales[Amount])", avgSales.Expression);
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
    public void ComposeModel_ModelLevelMeasure_ShouldOverrideTableLevelMeasure()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" }
                },
                Measures = new List<MeasureDefinition>
                {
                    new()
                    {
                        Name = "Total Sales",
                        Table = "Sales",
                        Expression = "SUM(Sales[Amount])",
                        FormatString = "$#,##0.00"
                    }
                }
            };

            _serializer.SaveToFile(tableDef, Path.Combine(tempPath, "sales.yaml"));
            registry.LoadTables(tempPath);

            // Model-level measure with same name but different expression
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } },
                Measures = new List<MeasureDefinition>
                {
                    new()
                    {
                        Name = "Total Sales",
                        Table = "Sales",
                        Expression = "CALCULATE(SUM(Sales[Amount]), ALLSELECTED())",
                        FormatString = "#,##0"
                    }
                }
            };

            var composer = new ModelComposer(registry);

            // Act
            var database = composer.ComposeModel(modelDef);

            // Assert - model-level measure wins
            var table = database.Model.Tables.Find("Sales");
            Assert.NotNull(table);
            Assert.Single(table.Measures);

            var measure = table.Measures.Find("Total Sales");
            Assert.NotNull(measure);
            Assert.Equal("CALCULATE(SUM(Sales[Amount]), ALLSELECTED())", measure.Expression);
            Assert.Equal("#,##0", measure.FormatString);
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
    public void ComposeModel_RelationshipOnCalculatedColumn_ShouldWork()
    {
        // Arrange
        var registry = new TableRegistry(_serializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var factTable = new TableDefinition
            {
                Name = "FactSales",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" },
                    // Calculated column used in relationship
                    new()
                    {
                        Name = "YearMonth",
                        Type = "String",
                        Expression = "FORMAT([SaleDate], \"YYYYMM\")"
                    }
                }
            };

            var dimTable = new TableDefinition
            {
                Name = "DimDate",
                MExpression = "let Source = #table(...) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new()
                    {
                        Name = "YearMonth",
                        Type = "String",
                        Expression = "FORMAT([Date], \"YYYYMM\")"
                    }
                }
            };

            _serializer.SaveToFile(factTable, Path.Combine(tempPath, "fact_sales.yaml"));
            _serializer.SaveToFile(dimTable, Path.Combine(tempPath, "dim_date.yaml"));
            registry.LoadTables(tempPath);

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "FactSales" },
                    new() { Ref = "DimDate" }
                },
                Relationships = new List<RelationshipDefinition>
                {
                    new()
                    {
                        FromTable = "FactSales",
                        FromColumn = "YearMonth",
                        ToTable = "DimDate",
                        ToColumn = "YearMonth",
                        Cardinality = "ManyToOne"
                    }
                }
            };

            var composer = new ModelComposer(registry);

            // Act
            var database = composer.ComposeModel(modelDef);

            // Assert
            Assert.Single(database.Model.Relationships);
            var relationship = database.Model.Relationships[0] as SingleColumnRelationship;
            Assert.NotNull(relationship);
            Assert.NotNull(relationship.FromColumn);
            Assert.NotNull(relationship.ToColumn);
            Assert.Equal("YearMonth", relationship.FromColumn.Name);
            Assert.Equal("YearMonth", relationship.ToColumn.Name);
            Assert.IsType<CalculatedColumn>(relationship.FromColumn);
            Assert.IsType<CalculatedColumn>(relationship.ToColumn);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
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
