using CsvHelper.Configuration.Attributes;

namespace Pbt.Core.Models;

/// <summary>
/// Represents a row from a CSV schema export (e.g., information_schema from Snowflake/SQL Server)
/// </summary>
public class CsvSchemaRow
{
    /// <summary>
    /// Table catalog/database (e.g., "master", "PRD_DW_ANALYTICS")
    /// </summary>
    [Name("table_catalog")]
    [Optional]
    public string? TableCatalog { get; set; }

    /// <summary>
    /// Table schema (e.g., "dbo", "sales")
    /// </summary>
    [Name("table_schema")]
    [Optional]
    public string? TableSchema { get; set; }

    /// <summary>
    /// Table name
    /// </summary>
    [Name("table_name")]
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Column name
    /// </summary>
    [Name("column_name")]
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>
    /// Data type (database-specific, e.g., "VARCHAR", "NUMBER", "datetime2")
    /// </summary>
    [Name("data_type")]
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Ordinal position of the column
    /// </summary>
    [Name("ordinal_position")]
    [Optional]
    public int? OrdinalPosition { get; set; }

    /// <summary>
    /// Is the column nullable
    /// </summary>
    [Name("is_nullable")]
    [Optional]
    public string? IsNullable { get; set; }

    /// <summary>
    /// Column default value
    /// </summary>
    [Name("column_default")]
    [Optional]
    public string? ColumnDefault { get; set; }

    /// <summary>
    /// Column comment/description
    /// </summary>
    [Name("column_comment")]
    [Optional]
    public string? ColumnComment { get; set; }

    /// <summary>
    /// Table comment/description
    /// </summary>
    [Name("table_comment")]
    [Optional]
    public string? TableComment { get; set; }
}
