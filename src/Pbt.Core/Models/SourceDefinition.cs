namespace Pbt.Core.Models;

/// <summary>
/// Optional source metadata (alternative to MExpression)
/// Used by PbiScaffold, can be converted to MExpression by PbiComposer at build time
/// </summary>
public class SourceDefinition
{
    /// <summary>
    /// Source type: "snowflake", "sqlserver", "powerquery", etc.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Named connection reference (e.g., "DataWarehouse")
    /// Can be either a direct connection string or a reference to a shared connector expression
    /// </summary>
    public string? Connection { get; set; }

    /// <summary>
    /// Reference to a shared connector expression name (e.g., "SnowflakeSource")
    /// If specified, uses the shared connector instead of creating inline connection
    /// </summary>
    public string? Connector { get; set; }

    /// <summary>
    /// Database/catalog name (e.g., "master", "PRD_DW_ANALYTICS")
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// Schema name (e.g., "dbo", "sales")
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Table name
    /// </summary>
    public string? Table { get; set; }

    /// <summary>
    /// Custom SQL query (alternative to Schema+Table)
    /// </summary>
    public string? Query { get; set; }
}
