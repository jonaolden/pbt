using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class TomConverterTests
{
    [Fact]
    public void ToTableDefinition_DataColumns_ShouldExtractAllProperties()
    {
        var table = new Table { Name = "Sales", Description = "Fact table" };
        var col = new DataColumn
        {
            Name = "Amount",
            DataType = DataType.Decimal,
            Description = "Order amount",
            SourceColumn = "AMOUNT",
            FormatString = "$#,##0.00",
            IsHidden = false,
            DisplayFolder = "Financials",
            LineageTag = "abc-123"
        };
        table.Columns.Add(col);

        var result = TomConverter.ToTableDefinition(table, includeLineageTags: true);

        Assert.Equal("Sales", result.Name);
        Assert.Equal("Fact table", result.Description);
        Assert.Single(result.Columns);

        var colDef = result.Columns[0];
        Assert.Equal("Amount", colDef.Name);
        Assert.Equal("Decimal", colDef.Type);
        Assert.Equal("Order amount", colDef.Description);
        Assert.Equal("AMOUNT", colDef.SourceColumn);
        Assert.Equal("$#,##0.00", colDef.FormatString);
        Assert.Equal("Financials", colDef.DisplayFolder);
        Assert.Equal("abc-123", colDef.LineageTag);
    }

    [Fact]
    public void ToTableDefinition_WithoutLineageTags_ShouldOmitTags()
    {
        var table = new Table { Name = "Sales" };
        table.Columns.Add(new DataColumn
        {
            Name = "ID",
            DataType = DataType.Int64,
            SourceColumn = "ID",
            LineageTag = "should-be-excluded"
        });
        table.LineageTag = "table-tag";

        var result = TomConverter.ToTableDefinition(table, includeLineageTags: false);

        Assert.Null(result.LineageTag);
        Assert.Null(result.Columns[0].LineageTag);
    }

    [Fact]
    public void ToTableDefinition_CalculatedColumn_ShouldExtractExpression()
    {
        var table = new Table { Name = "Sales" };
        table.Columns.Add(new CalculatedColumn
        {
            Name = "IsLarge",
            DataType = DataType.Boolean,
            Expression = "IF([Amount] > 1000, TRUE(), FALSE())"
        });

        var result = TomConverter.ToTableDefinition(table);

        Assert.Single(result.Columns);
        Assert.Equal("IF([Amount] > 1000, TRUE(), FALSE())", result.Columns[0].Expression);
    }

    [Fact]
    public void ToTableDefinition_WithMeasures_ShouldExtractAll()
    {
        var table = new Table { Name = "Sales" };
        table.Measures.Add(new Measure
        {
            Name = "Total Sales",
            Expression = "SUM(Sales[Amount])",
            FormatString = "$#,##0",
            DisplayFolder = "KPIs"
        });

        var result = TomConverter.ToTableDefinition(table);

        Assert.Single(result.Measures);
        Assert.Equal("Total Sales", result.Measures[0].Name);
        Assert.Equal("SUM(Sales[Amount])", result.Measures[0].Expression);
        Assert.Equal("Sales", result.Measures[0].Table);
    }

    [Fact]
    public void ToTableDefinition_WithHierarchy_ShouldExtractLevels()
    {
        var table = new Table { Name = "Geo" };
        var countryCol = new DataColumn { Name = "Country", DataType = DataType.String, SourceColumn = "Country" };
        var cityCol = new DataColumn { Name = "City", DataType = DataType.String, SourceColumn = "City" };
        table.Columns.Add(countryCol);
        table.Columns.Add(cityCol);

        var hierarchy = new Hierarchy { Name = "Geography", Description = "Geo drill" };
        hierarchy.Levels.Add(new Level { Name = "Country", Column = countryCol });
        hierarchy.Levels.Add(new Level { Name = "City", Column = cityCol });
        table.Hierarchies.Add(hierarchy);

        var result = TomConverter.ToTableDefinition(table);

        Assert.Single(result.Hierarchies);
        Assert.Equal("Geography", result.Hierarchies[0].Name);
        Assert.Equal(2, result.Hierarchies[0].Levels.Count);
        Assert.Equal("Country", result.Hierarchies[0].Levels[0].Column);
        Assert.Equal("City", result.Hierarchies[0].Levels[1].Column);
    }

    [Fact]
    public void ToTableDefinition_WithMPartition_ShouldExtractExpression()
    {
        var table = new Table { Name = "Sales" };
        var partition = new Partition
        {
            Name = "Sales_Part",
            Source = new MPartitionSource { Expression = "let Source = ... in Source" }
        };
        table.Partitions.Add(partition);

        var result = TomConverter.ToTableDefinition(table);

        Assert.Equal("let Source = ... in Source", result.MExpression);
    }

    [Fact]
    public void ToModelDefinition_ShouldExtractTablesAndRelationships()
    {
        var database = new Database { Name = "Analytics", CompatibilityLevel = 1700 };
        database.Model = new Model();
        database.Model.Tables.Add(new Table { Name = "Sales" });
        database.Model.Tables.Add(new Table { Name = "Customers" });

        var result = TomConverter.ToModelDefinition(database);

        Assert.Equal("Analytics", result.Name);
        Assert.Equal(1700, result.CompatibilityLevel);
        Assert.Equal(2, result.Tables.Count);
        Assert.Equal("Sales", result.Tables[0].Ref);
        Assert.Equal("Customers", result.Tables[1].Ref);
    }

    [Fact]
    public void MapCardinality_AllCombinations_ShouldMapCorrectly()
    {
        Assert.Equal("ManyToOne", TomConverter.MapCardinality(
            RelationshipEndCardinality.Many, RelationshipEndCardinality.One));
        Assert.Equal("OneToMany", TomConverter.MapCardinality(
            RelationshipEndCardinality.One, RelationshipEndCardinality.Many));
        Assert.Equal("OneToOne", TomConverter.MapCardinality(
            RelationshipEndCardinality.One, RelationshipEndCardinality.One));
        Assert.Equal("ManyToMany", TomConverter.MapCardinality(
            RelationshipEndCardinality.Many, RelationshipEndCardinality.Many));
    }

    [Fact]
    public void MapCrossFilterDirection_AllValues_ShouldMapCorrectly()
    {
        Assert.Equal("Single", TomConverter.MapCrossFilterDirection(CrossFilteringBehavior.OneDirection));
        Assert.Equal("Both", TomConverter.MapCrossFilterDirection(CrossFilteringBehavior.BothDirections));
        Assert.Equal("Automatic", TomConverter.MapCrossFilterDirection(CrossFilteringBehavior.Automatic));
    }
}
