using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class ColumnFilterTests
{
    [Fact]
    public void GenerateTable_WithExcludePatterns_ShouldFilterColumns()
    {
        var config = ScaffoldConfig.CreateDefault();
        var sourceConfig = SourceTypeMapper.CreateDefaultSnowflakeConfig();
        sourceConfig.ColumnNaming = new ColumnNamingConfig
        {
            ExcludePatterns = new List<string> { "^DW_", "_HASH$" }
        };

        var generator = new TableGenerator(config, sourceConfig);
        var rows = new List<CsvSchemaRow>
        {
            new() { TableName = "SALES", ColumnName = "ORDER_ID", DataType = "NUMBER" },
            new() { TableName = "SALES", ColumnName = "DW_CREATED_AT", DataType = "TIMESTAMP_NTZ" },
            new() { TableName = "SALES", ColumnName = "AMOUNT", DataType = "NUMBER" },
            new() { TableName = "SALES", ColumnName = "ROW_HASH", DataType = "VARCHAR" }
        };

        var result = generator.GenerateTable("SALES", rows);

        Assert.Equal(2, result.Columns.Count);
        Assert.Contains(result.Columns, c => c.SourceColumn == "ORDER_ID");
        Assert.Contains(result.Columns, c => c.SourceColumn == "AMOUNT");
        Assert.DoesNotContain(result.Columns, c => c.SourceColumn == "DW_CREATED_AT");
        Assert.DoesNotContain(result.Columns, c => c.SourceColumn == "ROW_HASH");
    }

    [Fact]
    public void GenerateTable_WithIncludePatterns_ShouldOnlyIncludeMatching()
    {
        var config = ScaffoldConfig.CreateDefault();
        var sourceConfig = SourceTypeMapper.CreateDefaultSnowflakeConfig();
        sourceConfig.ColumnNaming = new ColumnNamingConfig
        {
            IncludePatterns = new List<string> { "_ID$", "^AMOUNT$", "^NAME$" }
        };

        var generator = new TableGenerator(config, sourceConfig);
        var rows = new List<CsvSchemaRow>
        {
            new() { TableName = "SALES", ColumnName = "ORDER_ID", DataType = "NUMBER" },
            new() { TableName = "SALES", ColumnName = "CUSTOMER_ID", DataType = "NUMBER" },
            new() { TableName = "SALES", ColumnName = "AMOUNT", DataType = "NUMBER" },
            new() { TableName = "SALES", ColumnName = "SOME_FLAG", DataType = "BOOLEAN" }
        };

        var result = generator.GenerateTable("SALES", rows);

        Assert.Equal(3, result.Columns.Count);
        Assert.DoesNotContain(result.Columns, c => c.SourceColumn == "SOME_FLAG");
    }

    [Fact]
    public void GenerateTable_ExcludeBeforeInclude_ShouldExcludeFirst()
    {
        var config = ScaffoldConfig.CreateDefault();
        var sourceConfig = SourceTypeMapper.CreateDefaultSnowflakeConfig();
        sourceConfig.ColumnNaming = new ColumnNamingConfig
        {
            ExcludePatterns = new List<string> { "^DW_" },
            IncludePatterns = new List<string> { "." } // Include all (except excluded)
        };

        var generator = new TableGenerator(config, sourceConfig);
        var rows = new List<CsvSchemaRow>
        {
            new() { TableName = "T", ColumnName = "ID", DataType = "NUMBER" },
            new() { TableName = "T", ColumnName = "DW_LOAD_TS", DataType = "TIMESTAMP_NTZ" }
        };

        var result = generator.GenerateTable("T", rows);

        Assert.Single(result.Columns);
        Assert.Equal("ID", result.Columns[0].SourceColumn);
    }

    [Fact]
    public void GenerateTable_NoFilters_ShouldIncludeAll()
    {
        var config = ScaffoldConfig.CreateDefault();
        var sourceConfig = SourceTypeMapper.CreateDefaultSnowflakeConfig();

        var generator = new TableGenerator(config, sourceConfig);
        var rows = new List<CsvSchemaRow>
        {
            new() { TableName = "T", ColumnName = "A", DataType = "NUMBER" },
            new() { TableName = "T", ColumnName = "B", DataType = "VARCHAR" }
        };

        var result = generator.GenerateTable("T", rows);

        Assert.Equal(2, result.Columns.Count);
    }
}
