using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class RelationshipShorthandParserTests
{
    [Fact]
    public void TryParseShorthand_ValidPattern_ShouldParseCorrectly()
    {
        var result = RelationshipShorthandParser.TryParseShorthand("Sales.CustomerID -> Customers.CustomerID");

        Assert.NotNull(result);
        Assert.Equal("Sales", result.FromTable);
        Assert.Equal("CustomerID", result.FromColumn);
        Assert.Equal("Customers", result.ToTable);
        Assert.Equal("CustomerID", result.ToColumn);
    }

    [Fact]
    public void TryParseShorthand_ValidPattern_ShouldSetDefaults()
    {
        var result = RelationshipShorthandParser.TryParseShorthand("Sales.FK -> Dim.PK");

        Assert.NotNull(result);
        Assert.Equal("ManyToOne", result.Cardinality);
        Assert.Equal("Single", result.CrossFilterDirection);
        Assert.True(result.Active);
    }

    [Fact]
    public void TryParseShorthand_NoSpaces_ShouldParse()
    {
        var result = RelationshipShorthandParser.TryParseShorthand("A.B->C.D");

        Assert.NotNull(result);
        Assert.Equal("A", result.FromTable);
        Assert.Equal("D", result.ToColumn);
    }

    [Fact]
    public void TryParseShorthand_ExtraWhitespace_ShouldParse()
    {
        var result = RelationshipShorthandParser.TryParseShorthand("  Sales.ID  ->  Customers.ID  ");

        Assert.NotNull(result);
        Assert.Equal("Sales", result.FromTable);
    }

    [Theory]
    [InlineData("not a relationship")]
    [InlineData("Sales -> Customers")]
    [InlineData("Sales.ID - Customers.ID")]
    [InlineData("")]
    public void TryParseShorthand_InvalidPattern_ShouldReturnNull(string input)
    {
        Assert.Null(RelationshipShorthandParser.TryParseShorthand(input));
    }

    [Theory]
    [InlineData("Sales.ID -> Customers.ID", true)]
    [InlineData("Sales.ID->Customers.ID", true)]
    [InlineData("Sales.ID", false)]
    [InlineData("just text", false)]
    public void IsShorthand_ShouldDetectArrow(string input, bool expected)
    {
        Assert.Equal(expected, RelationshipShorthandParser.IsShorthand(input));
    }
}
