namespace Pbt.Core.Models;

/// <summary>
/// Import configuration for live schema extraction from a data source.
/// Parsed from the 'import' section of a source config YAML file.
/// Source-agnostic — works for Snowflake, SQL Server, etc.
/// </summary>
public class SourceImportConfig
{
    /// <summary>
    /// Database/catalog to query (e.g., "ANALYTICS_DB", "master").
    /// Used as the catalog qualifier for INFORMATION_SCHEMA queries.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Schema to query (e.g., "PUBLIC", "dbo").
    /// Filters INFORMATION_SCHEMA by TABLE_SCHEMA.
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// Table names to import. Use ["*"] or leave empty to import all tables.
    /// Names are matched case-insensitively.
    /// </summary>
    public List<string> Tables { get; set; } = new();

    /// <summary>
    /// If true, also import views in addition to base tables.
    /// Default: false.
    /// </summary>
    public bool IncludeViews { get; set; } = false;

    /// <summary>
    /// Whether to import all tables (Tables contains "*" or is empty).
    /// </summary>
    public bool ImportAllTables =>
        Tables.Count == 0 || (Tables.Count == 1 && Tables[0] == "*");
}
