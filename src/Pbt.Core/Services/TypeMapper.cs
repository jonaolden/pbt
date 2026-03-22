using System.Text.RegularExpressions;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Maps database types to Power BI types using configuration
/// </summary>
public class TypeMapper
{
    private readonly ScaffoldConfig _config;

    public TypeMapper(ScaffoldConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Map database type to Power BI type
    /// </summary>
    public string MapType(string databaseType, string columnName)
    {
        // First, check column overrides (regex-based, applied in order)
        foreach (var override_ in _config.ColumnOverrides)
        {
            if (Regex.IsMatch(columnName, override_.Pattern, RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrEmpty(override_.Type))
                {
                    return override_.Type;
                }
            }
        }

        // Extract base type (remove size/precision info)
        var baseType = ExtractBaseType(databaseType);

        // Check type mappings
        if (_config.TypeMappings.TryGetValue(baseType, out var pbiType))
        {
            return pbiType;
        }

        // Default to string for unknown types
        Console.WriteLine($"Warning: Unknown type '{databaseType}' for column '{columnName}', defaulting to string");
        return "string";
    }

    /// <summary>
    /// Get format string from column overrides if specified
    /// </summary>
    public string? GetFormatString(string columnName)
    {
        foreach (var override_ in _config.ColumnOverrides)
        {
            if (Regex.IsMatch(columnName, override_.Pattern, RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrEmpty(override_.FormatString))
                {
                    return override_.FormatString;
                }
            }
        }

        return null;
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
}
