namespace Pbt.Core.Models;

/// <summary>
/// Configuration for scaffolding table definitions from CSV schema exports
/// </summary>
public class ScaffoldConfig
{
    /// <summary>
    /// Type mappings from database types to Power BI types
    /// </summary>
    public Dictionary<string, string> TypeMappings { get; set; } = new();

    /// <summary>
    /// Column override rules (regex-based, applied in order)
    /// </summary>
    public List<ColumnOverride> ColumnOverrides { get; set; } = new();

    /// <summary>
    /// Table rules (regex-based)
    /// </summary>
    public List<TableRule> TableRules { get; set; } = new();

    /// <summary>
    /// Naming conventions
    /// </summary>
    public NamingConfig Naming { get; set; } = new();

    /// <summary>
    /// Source connection metadata
    /// </summary>
    public SourceConfig? Source { get; set; }

    /// <summary>
    /// Creates default configuration with built-in Snowflake and SQL Server type mappings
    /// </summary>
    public static ScaffoldConfig CreateDefault()
    {
        return new ScaffoldConfig
        {
            TypeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Snowflake types
                ["VARCHAR"] = "String",
                ["TEXT"] = "String",
                ["CHAR"] = "String",
                ["STRING"] = "String",
                ["NUMBER"] = "Int64",
                ["DECIMAL"] = "Decimal",
                ["NUMERIC"] = "Decimal",
                ["FLOAT"] = "Double",
                ["DOUBLE"] = "Double",
                ["TIMESTAMP_NTZ"] = "DateTime",
                ["TIMESTAMP_LTZ"] = "DateTime",
                ["TIMESTAMP_TZ"] = "DateTime",
                ["TIMESTAMP"] = "DateTime",
                ["DATE"] = "DateTime",
                ["TIME"] = "DateTime",
                ["BOOLEAN"] = "Boolean",
                ["VARIANT"] = "String",
                ["OBJECT"] = "String",
                ["ARRAY"] = "String",

                // SQL Server types
                ["nvarchar"] = "String",
                ["varchar"] = "String",
                ["nchar"] = "String",
                ["char"] = "String",
                ["text"] = "String",
                ["ntext"] = "String",
                ["int"] = "Int64",
                ["bigint"] = "Int64",
                ["smallint"] = "Int64",
                ["tinyint"] = "Int64",
                ["decimal"] = "Decimal",
                ["numeric"] = "Decimal",
                ["money"] = "Decimal",
                ["smallmoney"] = "Decimal",
                ["float"] = "Double",
                ["real"] = "Double",
                ["datetime"] = "DateTime",
                ["datetime2"] = "DateTime",
                ["smalldatetime"] = "DateTime",
                ["date"] = "DateTime",
                ["time"] = "DateTime",
                ["datetimeoffset"] = "DateTime",
                ["bit"] = "Boolean",
                ["uniqueidentifier"] = "String",
                ["xml"] = "String"
            },

            ColumnOverrides = new List<ColumnOverride>
            {
                new ColumnOverride
                {
                    Pattern = ".*_id$",
                    Type = "Int64"
                },
                new ColumnOverride
                {
                    Pattern = ".*_date$",
                    Type = "DateTime"
                },
                new ColumnOverride
                {
                    Pattern = ".*_amount$",
                    Type = "Decimal",
                    FormatString = "$#,##0.00"
                },
                new ColumnOverride
                {
                    Pattern = "^is_.*",
                    Type = "Boolean"
                }
            },

            TableRules = new List<TableRule>
            {
                new TableRule
                {
                    Pattern = "^stg_.*",
                    IsHidden = true,
                    PrefixRemove = "stg_"
                },
                new TableRule
                {
                    Pattern = "^dim_.*",
                    PrefixRemove = "dim_"
                },
                new TableRule
                {
                    Pattern = "^fact_.*",
                    IsHidden = true,
                    PrefixRemove = "fact_"
                }
            },

            Naming = new NamingConfig
            {
                TableNameFormat = "PascalCase",
                ColumnNameFormat = "PascalCase",
                PreservePatterns = new List<string>
                {
                    ".*_id$",
                    ".*_key$"
                }
            }
        };
    }
}

/// <summary>
/// Column override rule
/// </summary>
public class ColumnOverride
{
    public string Pattern { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? FormatString { get; set; }
}

/// <summary>
/// Table rule for renaming and hiding
/// </summary>
public class TableRule
{
    public string Pattern { get; set; } = string.Empty;
    public bool? IsHidden { get; set; }
    public string? PrefixRemove { get; set; }
}

/// <summary>
/// Naming convention configuration
/// </summary>
public class NamingConfig
{
    public string TableNameFormat { get; set; } = "PascalCase";
    public string ColumnNameFormat { get; set; } = "PascalCase";
    public List<string> PreservePatterns { get; set; } = new();
}

/// <summary>
/// Source connection configuration
/// </summary>
public class SourceConfig
{
    public string Type { get; set; } = string.Empty;
    public string? Connection { get; set; }
}
