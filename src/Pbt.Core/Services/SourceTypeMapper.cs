using System.Text.RegularExpressions;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Maps database types to M and TMDL types using source-specific configuration
/// Supports dual type conversion for query folding optimization
/// </summary>
public class SourceTypeMapper
{
    private readonly SourceTypeConfig _config;

    public SourceTypeMapper(SourceTypeConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Result of type mapping containing both M and TMDL types
    /// </summary>
    public record TypeMappingResult(string MType, string TmdlType);

    /// <summary>
    /// Map database type to M and TMDL types
    /// </summary>
    public TypeMappingResult MapType(string databaseType, string columnName)
    {
        // First, check regex overrides (pattern-based, applied in order)
        foreach (var override_ in _config.Datatypes.RegexOverrides)
        {
            foreach (var pattern in override_.Pattern)
            {
                if (Regex.IsMatch(columnName, pattern, RegexOptions.IgnoreCase))
                {
                    return new TypeMappingResult(override_.MType, override_.TmdlType);
                }
            }
        }

        // Extract base type (remove size/precision info)
        var baseType = ExtractBaseType(databaseType);

        // Check type mappings
        var mapping = _config.Datatypes.Mappings
            .FirstOrDefault(m => string.Equals(m.DatabaseType, baseType, StringComparison.OrdinalIgnoreCase));

        if (mapping != null)
        {
            return new TypeMappingResult(mapping.MType, mapping.TmdlType);
        }

        // Default to Text.Type and string for unknown types
        Console.WriteLine($"Warning: Unknown type '{databaseType}' for column '{columnName}', defaulting to Text.Type/string");
        return new TypeMappingResult("Text.Type", "string");
    }

    /// <summary>
    /// Extract base type from database type string
    /// Examples:
    /// - "VARCHAR(255)" -> "VARCHAR"
    /// - "DECIMAL(10,2)" -> "DECIMAL"
    /// - "NUMBER" -> "NUMBER"
    /// </summary>
    private string ExtractBaseType(string databaseType)
    {
        var match = Regex.Match(databaseType, @"^([A-Za-z_]+)");
        return match.Success ? match.Groups[1].Value : databaseType;
    }

    /// <summary>
    /// Creates default Snowflake configuration
    /// </summary>
    public static SourceTypeConfig CreateDefaultSnowflakeConfig()
    {
        return new SourceTypeConfig
        {
            Datatypes = new DatatypesConfig
            {
                Mappings = new List<TypeMapping>
                {
                    new() { DatabaseType = "NUMBER", MType = "Int32.Type", TmdlType = "int64" },
                    new() { DatabaseType = "STRING", MType = "Text.Type", TmdlType = "string" },
                    new() { DatabaseType = "INT", MType = "Int32.Type", TmdlType = "int64" },
                    new() { DatabaseType = "INTEGER", MType = "Int32.Type", TmdlType = "int64" },
                    new() { DatabaseType = "SMALLINT", MType = "Int32.Type", TmdlType = "int64" },
                    new() { DatabaseType = "BIGINT", MType = "Int32.Type", TmdlType = "int64" },
                    new() { DatabaseType = "BYTEINT", MType = "Int32.Type", TmdlType = "int64" },
                    new() { DatabaseType = "DECIMAL", MType = "Decimal.Type", TmdlType = "decimal" },
                    new() { DatabaseType = "NUMERIC", MType = "Decimal.Type", TmdlType = "decimal" },
                    new() { DatabaseType = "FLOAT", MType = "Decimal.Type", TmdlType = "decimal" },
                    new() { DatabaseType = "DOUBLE", MType = "Decimal.Type", TmdlType = "decimal" },
                    new() { DatabaseType = "VARCHAR", MType = "Text.Type", TmdlType = "string" },
                    new() { DatabaseType = "TEXT", MType = "Text.Type", TmdlType = "string" },
                    new() { DatabaseType = "CHAR", MType = "Text.Type", TmdlType = "string" },
                    new() { DatabaseType = "BOOLEAN", MType = "Logical.Type", TmdlType = "boolean" },
                    new() { DatabaseType = "DATE", MType = "Date.Type", TmdlType = "dateTime" },
                    new() { DatabaseType = "DATETIME", MType = "DateTime.Type", TmdlType = "dateTime" },
                    new() { DatabaseType = "TIMESTAMP", MType = "DateTime.Type", TmdlType = "dateTime" },
                    new() { DatabaseType = "TIMESTAMP_NTZ", MType = "DateTime.Type", TmdlType = "dateTime" },
                    new() { DatabaseType = "TIMESTAMP_LTZ", MType = "DateTime.Type", TmdlType = "dateTime" },
                    new() { DatabaseType = "TIMESTAMP_TZ", MType = "DateTime.Type", TmdlType = "dateTime" },
                    new() { DatabaseType = "TIME", MType = "Time.Type", TmdlType = "dateTime" },
                    new() { DatabaseType = "VARIANT", MType = "Text.Type", TmdlType = "string" },
                    new() { DatabaseType = "OBJECT", MType = "Text.Type", TmdlType = "string" },
                    new() { DatabaseType = "ARRAY", MType = "Text.Type", TmdlType = "string" }
                },
                RegexOverrides = new List<RegexOverride>
                {
                    new()
                    {
                        Pattern = new List<string> { "_ID$", "_ID_", "_KEY$", "_CODE$", "_CODE_" },
                        MType = "Text.Type",
                        TmdlType = "string"
                    },
                    new()
                    {
                        Pattern = new List<string> { "_QTY$" },
                        MType = "Int32.Type",
                        TmdlType = "int64"
                    }
                }
            }
        };
    }
}
