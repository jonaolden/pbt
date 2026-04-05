using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class NamingConverterTests
{
    private static NamingConverter CreateConverter(
        string tableFormat = "PascalCase",
        string columnFormat = "PascalCase",
        List<string>? preservePatterns = null,
        List<TableRule>? tableRules = null)
    {
        var config = ScaffoldConfig.CreateDefault();
        config.Naming = new NamingConfig
        {
            TableNameFormat = tableFormat,
            ColumnNameFormat = columnFormat,
            PreservePatterns = preservePatterns ?? new List<string>()
        };
        config.TableRules = tableRules ?? new List<TableRule>();
        return new NamingConverter(config);
    }

    [Theory]
    [InlineData("customer_name", "CustomerName")]
    [InlineData("CUSTOMER_NAME", "CustomerName")]
    [InlineData("order_id", "OrderId")]
    [InlineData("id", "Id")]
    [InlineData("AMOUNT", "Amount")]
    public void ConvertColumnName_PascalCase_ShouldConvert(string input, string expected)
    {
        var converter = CreateConverter();
        Assert.Equal(expected, converter.ConvertColumnName(input));
    }

    [Theory]
    [InlineData("FACT_SALES", "FactSales")]
    [InlineData("dim_customer", "DimCustomer")]
    [InlineData("STG_ORDERS", "StgOrders")]
    public void ConvertTableName_PascalCase_ShouldConvert(string input, string expected)
    {
        var converter = CreateConverter();
        Assert.Equal(expected, converter.ConvertTableName(input));
    }

    [Fact]
    public void ConvertTableName_Keep_ShouldPreserveOriginal()
    {
        var converter = CreateConverter(tableFormat: "keep");
        Assert.Equal("FACT_SALES", converter.ConvertTableName("FACT_SALES"));
    }

    [Fact]
    public void ConvertColumnName_PreservePattern_ShouldNotConvert()
    {
        var converter = CreateConverter(preservePatterns: new List<string> { "^ID$", "^SK_" });

        Assert.Equal("ID", converter.ConvertColumnName("ID"));
        Assert.Equal("SK_CUSTOMER", converter.ConvertColumnName("SK_CUSTOMER"));
        Assert.Equal("CustomerName", converter.ConvertColumnName("CUSTOMER_NAME")); // Not matched
    }

    [Fact]
    public void ConvertTableName_WithPrefixRemoval_ShouldStripPrefix()
    {
        var converter = CreateConverter(tableRules: new List<TableRule>
        {
            new() { Pattern = "^FACT_", PrefixRemove = "FACT_" },
            new() { Pattern = "^DIM_", PrefixRemove = "DIM_" }
        });

        Assert.Equal("Sales", converter.ConvertTableName("FACT_SALES"));
        Assert.Equal("Customer", converter.ConvertTableName("DIM_CUSTOMER"));
        Assert.Equal("StgOrders", converter.ConvertTableName("STG_ORDERS")); // No matching rule
    }

    [Fact]
    public void ShouldHideTable_WithHiddenRule_ShouldReturnTrue()
    {
        var converter = CreateConverter(tableRules: new List<TableRule>
        {
            new() { Pattern = "^STG_", IsHidden = true }
        });

        Assert.True(converter.ShouldHideTable("STG_ORDERS"));
        Assert.False(converter.ShouldHideTable("FACT_SALES"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void ConvertColumnName_EmptyOrWhitespace_ShouldReturnAsIs(string input, string expected)
    {
        var converter = CreateConverter();
        Assert.Equal(expected, converter.ConvertColumnName(input));
    }

    [Fact]
    public void ConvertColumnName_SnakeCase_ShouldConvert()
    {
        var converter = CreateConverter(columnFormat: "snake_case");
        Assert.Equal("customer_name", converter.ConvertColumnName("CustomerName"));
    }
}
