namespace Pbt.Core.Models;

/// <summary>
/// Import filter configuration for Snowflake metadata extraction.
/// Parsed from the 'import' section of a source config YAML file.
/// </summary>
public class SnowflakeImportConfig
{
    /// <summary>
    /// Snowflake database to query (e.g., "ANALYTICS_DB").
    /// Used as the catalog qualifier for INFORMATION_SCHEMA queries.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Snowflake schema to query (e.g., "PUBLIC", "ANALYTICS").
    /// Filters INFORMATION_SCHEMA.TABLES by TABLE_SCHEMA.
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// List of table names to import, or a single "*" entry to import all tables.
    /// Names are matched case-insensitively against TABLE_NAME in INFORMATION_SCHEMA.
    /// </summary>
    public List<string> Tables { get; set; } = new();

    /// <summary>
    /// If true, also import views (TABLE_TYPE = 'VIEW') in addition to base tables.
    /// Default: false (only 'BASE TABLE').
    /// </summary>
    public bool IncludeViews { get; set; } = false;

    /// <summary>
    /// Whether to import all tables (Tables contains "*" or is empty).
    /// </summary>
    public bool ImportAllTables =>
        Tables.Count == 0 || (Tables.Count == 1 && Tables[0] == "*");
}
