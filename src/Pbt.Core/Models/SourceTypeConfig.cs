namespace Pbt.Core.Models;

/// <summary>
/// Source-specific type mapping configuration (e.g., snowflake_config.yaml)
/// Supports dual type conversion: M types (for Power Query) and TMDL types (for Tabular Model)
/// </summary>
public class SourceTypeConfig
{
    /// <summary>
    /// Source type (e.g., "snowflake", "sqlserver")
    /// Required for generating proper source metadata
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Data type mappings and configuration
    /// </summary>
    public DatatypesConfig Datatypes { get; set; } = new();

    /// <summary>
    /// Connector configuration for shared data source expressions
    /// </summary>
    public ConnectorConfig? Connector { get; set; }

    /// <summary>
    /// Column naming configuration
    /// </summary>
    public ColumnNamingConfig? ColumnNaming { get; set; }
}

/// <summary>
/// Connector configuration for shared data source
/// </summary>
public class ConnectorConfig
{
    /// <summary>
    /// Connector name (used as expression name in TMDL)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Connection string or server address
    /// </summary>
    public string Connection { get; set; } = string.Empty;

    /// <summary>
    /// Optional warehouse (for Snowflake)
    /// </summary>
    public string? Warehouse { get; set; }

    /// <summary>
    /// Implementation version (e.g., "2.0" for Snowflake)
    /// </summary>
    public string? Implementation { get; set; }
}

/// <summary>
/// Datatypes configuration section
/// </summary>
public class DatatypesConfig
{
    /// <summary>
    /// Type mappings from database types to M and TMDL types
    /// </summary>
    public List<TypeMapping> Mappings { get; set; } = new();

    /// <summary>
    /// Regex-based overrides for specific column patterns
    /// </summary>
    public List<RegexOverride> RegexOverrides { get; set; } = new();
}

/// <summary>
/// Type mapping for a specific database type
/// </summary>
public class TypeMapping
{
    /// <summary>
    /// Database type name (e.g., "NUMBER", "VARCHAR", "TIMESTAMP_NTZ")
    /// </summary>
    public string DatabaseType { get; set; } = string.Empty;

    /// <summary>
    /// M type for Power Query (e.g., "Int32.Type", "Text.Type", "DateTime.Type")
    /// Used in Table.TransformColumnTypes for query folding optimization
    /// </summary>
    public string MType { get; set; } = string.Empty;

    /// <summary>
    /// TMDL type for Tabular Model (e.g., "int64", "string", "dateTime")
    /// Used in TOM DataColumn.DataType
    /// </summary>
    public string TmdlType { get; set; } = string.Empty;
}

/// <summary>
/// Regex-based type override for specific column name patterns
/// </summary>
public class RegexOverride
{
    /// <summary>
    /// Column name patterns to match (e.g., ["_ID$", "_ID_", "^DW_"])
    /// </summary>
    public List<string> Pattern { get; set; } = new();

    /// <summary>
    /// M type override
    /// </summary>
    public string MType { get; set; } = string.Empty;

    /// <summary>
    /// TMDL type override
    /// </summary>
    public string TmdlType { get; set; } = string.Empty;
}

/// <summary>
/// Column naming configuration
/// </summary>
public class ColumnNamingConfig
{
    /// <summary>
    /// Name conversion style (e.g., "pascalcase", "camelcase", "snake_case", "none")
    /// Default when no group matches
    /// </summary>
    public string? Conversion { get; set; }

    /// <summary>
    /// Regex patterns to preserve (don't apply conversion to matching columns)
    /// Default when no group matches
    /// </summary>
    public List<string> PreservePatterns { get; set; } = new();

    /// <summary>
    /// Regex-based column naming rules
    /// Default when no group matches
    /// </summary>
    public List<ColumnNamingRule> Rules { get; set; } = new();

    /// <summary>
    /// Table-specific naming groups (checked in order, first match wins)
    /// </summary>
    public List<ColumnNamingGroup> Groups { get; set; } = new();
}

/// <summary>
/// Table-specific column naming configuration group
/// </summary>
public class ColumnNamingGroup
{
    /// <summary>
    /// Regex pattern to match table names (e.g., "^FACT_", "^DIM_", "^STG_")
    /// </summary>
    public string TablePattern { get; set; } = string.Empty;

    /// <summary>
    /// Whether to hide tables matching this pattern
    /// </summary>
    public bool? TableIsHidden { get; set; }

    /// <summary>
    /// Table name conversion style (e.g., "pascalcase", "none")
    /// If not specified, uses default table naming from NamingConverter
    /// </summary>
    public string? TableNameConversion { get; set; }

    /// <summary>
    /// Column name conversion style for this group
    /// </summary>
    public string? Conversion { get; set; }

    /// <summary>
    /// Regex patterns to preserve for this group
    /// </summary>
    public List<string> PreservePatterns { get; set; } = new();

    /// <summary>
    /// Column naming rules for this group
    /// </summary>
    public List<ColumnNamingRule> Rules { get; set; } = new();
}

/// <summary>
/// Column naming rule based on regex pattern
/// </summary>
public class ColumnNamingRule
{
    /// <summary>
    /// Regex pattern to match column names
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Replacement name or template (supports $1, $2 for regex groups)
    /// </summary>
    public string? Replacement { get; set; }

    /// <summary>
    /// Whether to hide columns matching this pattern
    /// </summary>
    public bool? IsHidden { get; set; }

    /// <summary>
    /// Description to apply to matching columns
    /// </summary>
    public string? Description { get; set; }
}
