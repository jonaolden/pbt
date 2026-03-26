using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Converts database naming conventions (snake_case, UPPER_SNAKE) to Power BI naming (PascalCase)
/// </summary>
public sealed class NamingConverter
{
    private readonly ScaffoldConfig _config;

    public NamingConverter(ScaffoldConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Convert table name according to configuration
    /// </summary>
    public string ConvertTableName(string tableName)
    {
        // Apply table rules first (prefix removal, etc.)
        var processedName = tableName;

        foreach (var rule in _config.TableRules)
        {
            if (Regex.IsMatch(tableName, rule.Pattern, RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrEmpty(rule.PrefixRemove))
                {
                    if (processedName.StartsWith(rule.PrefixRemove, StringComparison.OrdinalIgnoreCase))
                    {
                        processedName = processedName.Substring(rule.PrefixRemove.Length);
                    }
                }
            }
        }

        // Apply naming format
        return ApplyNamingFormat(processedName, _config.Naming.TableNameFormat);
    }

    /// <summary>
    /// Convert column name according to configuration
    /// </summary>
    public string ConvertColumnName(string columnName)
    {
        // Check if column matches preserve patterns
        foreach (var pattern in _config.Naming.PreservePatterns)
        {
            if (Regex.IsMatch(columnName, pattern, RegexOptions.IgnoreCase))
            {
                return columnName;
            }
        }

        // Apply naming format
        return ApplyNamingFormat(columnName, _config.Naming.ColumnNameFormat);
    }

    /// <summary>
    /// Check if table should be hidden according to rules
    /// </summary>
    public bool ShouldHideTable(string tableName)
    {
        foreach (var rule in _config.TableRules)
        {
            if (Regex.IsMatch(tableName, rule.Pattern, RegexOptions.IgnoreCase))
            {
                if (rule.IsHidden.HasValue)
                {
                    return rule.IsHidden.Value;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Apply naming format conversion
    /// </summary>
    private string ApplyNamingFormat(string name, string format)
    {
        return format.ToLower() switch
        {
            "pascalcase" => ToPascalCase(name),
            "snake_case" => ToSnakeCase(name),
            "keep" => name,
            _ => ToPascalCase(name)
        };
    }

    /// <summary>
    /// Convert to PascalCase
    /// Examples:
    /// - "customer_name" -> "CustomerName"
    /// - "CUSTOMER_NAME" -> "CustomerName"
    /// - "customer_id" -> "CustomerId"
    /// </summary>
    private string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // Split on underscores and spaces
        var parts = input.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        var result = new StringBuilder();

        foreach (var part in parts)
        {
            if (part.Length == 0)
                continue;

            // Capitalize first letter, lowercase the rest
            result.Append(char.ToUpper(part[0], CultureInfo.InvariantCulture));

            if (part.Length > 1)
            {
                result.Append(part.Substring(1).ToLower(CultureInfo.InvariantCulture));
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Convert to snake_case
    /// </summary>
    private string ToSnakeCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // Insert underscore before uppercase letters (except first)
        var result = Regex.Replace(input, "(?<!^)([A-Z])", "_$1");

        return result.ToLower(CultureInfo.InvariantCulture);
    }
}
