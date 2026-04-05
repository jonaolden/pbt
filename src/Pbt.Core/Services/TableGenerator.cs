using System.Text.RegularExpressions;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Generates TableDefinition from CSV schema rows
/// </summary>
public sealed class TableGenerator
{
    private readonly ScaffoldConfig _config;
    private readonly TypeMapper? _typeMapper;
    private readonly SourceTypeMapper? _sourceTypeMapper;
    private readonly NamingConverter _namingConverter;
    private readonly SourceTypeConfig? _sourceTypeConfig;
    private ColumnNamingGroup? _currentNamingGroup;

    public TableGenerator(ScaffoldConfig config)
    {
        _config = config;
        _typeMapper = new TypeMapper(config);
        _sourceTypeMapper = null;
        _namingConverter = new NamingConverter(config);
    }

    public TableGenerator(ScaffoldConfig config, SourceTypeConfig sourceTypeConfig)
    {
        _config = config;
        _typeMapper = null;
        _sourceTypeMapper = new SourceTypeMapper(sourceTypeConfig);
        _namingConverter = new NamingConverter(config);
        _sourceTypeConfig = sourceTypeConfig;
    }

    /// <summary>
    /// Generate TableDefinition from CSV rows for a single table
    /// </summary>
    public TableDefinition GenerateTable(string tableName, List<CsvSchemaRow> rows)
    {
        if (rows.Count == 0)
        {
            throw new ArgumentException($"No rows provided for table: {tableName}", nameof(rows));
        }

        var firstRow = rows[0];

        // Find matching naming group for this table
        _currentNamingGroup = FindMatchingNamingGroup(tableName);

        // Determine table name based on group settings
        var convertedTableName = GetTableName(tableName);

        var tableDef = new TableDefinition
        {
            Name = convertedTableName,
            Description = firstRow.TableComment,
            IsHidden = _namingConverter.ShouldHideTable(tableName),
            Columns = new List<ColumnDefinition>(),
            Hierarchies = new List<HierarchyDefinition>()
        };

        // Override IsHidden from naming group if specified
        if (_currentNamingGroup?.TableIsHidden != null)
        {
            tableDef.IsHidden = _currentNamingGroup.TableIsHidden.Value;
        }

        // Add source metadata if configured
        if (_config.Source != null)
        {
            tableDef.Source = new SourceDefinition
            {
                Type = _config.Source.Type,
                Connection = _config.Source.Connection,
                Database = firstRow.TableCatalog,
                Schema = firstRow.TableSchema,
                Table = tableName
            };
        }

        // Add connector reference from source type config if available
        if (_sourceTypeConfig?.Connector != null && tableDef.Source != null)
        {
            tableDef.Source.Connector = _sourceTypeConfig.Connector.Name;
            // Don't need inline connection if using shared connector
            tableDef.Source.Connection = null;
        }

        // Filter columns by exclude/include patterns
        var filteredRows = FilterColumns(rows);

        // Generate columns
        foreach (var row in filteredRows)
        {
            var columnName = GetColumnName(row.ColumnName);

            ColumnDefinition column;

            // Use SourceTypeMapper if available (dual type mapping)
            if (_sourceTypeMapper != null)
            {
                var typeMapping = _sourceTypeMapper.MapType(row.DataType, row.ColumnName);
                column = new ColumnDefinition
                {
                    Name = columnName,
                    Type = typeMapping.TmdlType,
                    MType = typeMapping.MType,
                    SourceColumn = row.ColumnName,  // Keep original column name for source mapping
                    Description = row.ColumnComment
                };
            }
            // Fall back to legacy TypeMapper
            else if (_typeMapper != null)
            {
                var pbiType = _typeMapper.MapType(row.DataType, row.ColumnName);
                var formatString = _typeMapper.GetFormatString(row.ColumnName);
                column = new ColumnDefinition
                {
                    Name = columnName,
                    Type = pbiType,
                    SourceColumn = row.ColumnName,
                    Description = row.ColumnComment,
                    FormatString = formatString  // Legacy mapper still provides format strings
                };
            }
            else
            {
                throw new InvalidOperationException("No type mapper configured");
            }

            // Apply column naming rules from source config
            ApplyColumnNamingRules(column);

            tableDef.Columns.Add(column);
        }

        return tableDef;
    }

    /// <summary>
    /// Find matching naming group for a table
    /// </summary>
    private ColumnNamingGroup? FindMatchingNamingGroup(string tableName)
    {
        if (_sourceTypeConfig?.ColumnNaming?.Groups == null)
        {
            return null;
        }

        // Check groups in order, first match wins
        foreach (var group in _sourceTypeConfig.ColumnNaming.Groups)
        {
            if (Regex.IsMatch(tableName, group.TablePattern, RegexOptions.IgnoreCase))
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>
    /// Get table name based on group settings
    /// </summary>
    private string GetTableName(string sourceTableName)
    {
        // Check if group specifies table name conversion
        if (_currentNamingGroup?.TableNameConversion != null)
        {
            // If conversion is "none", keep original name
            if (_currentNamingGroup.TableNameConversion.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return sourceTableName;
            }
        }

        // Apply default naming conversion
        return _namingConverter.ConvertTableName(sourceTableName);
    }

    /// <summary>
    /// Get column name with preserve patterns applied
    /// </summary>
    private string GetColumnName(string sourceColumnName)
    {
        // Use group's preserve patterns if available, otherwise use default
        var preservePatterns = _currentNamingGroup?.PreservePatterns?.Count > 0
            ? _currentNamingGroup.PreservePatterns
            : _sourceTypeConfig?.ColumnNaming?.PreservePatterns;

        // Check if we should preserve the original name based on patterns
        if (preservePatterns != null)
        {
            foreach (var pattern in preservePatterns)
            {
                if (Regex.IsMatch(sourceColumnName, pattern, RegexOptions.IgnoreCase))
                {
                    return sourceColumnName;  // Keep original name
                }
            }
        }

        // Apply naming conversion from group or default
        var conversion = _currentNamingGroup?.Conversion ?? _sourceTypeConfig?.ColumnNaming?.Conversion;

        // If conversion is "none", keep original name
        if (conversion?.Equals("none", StringComparison.OrdinalIgnoreCase) == true)
        {
            return sourceColumnName;
        }

        // Apply naming conversion
        return _namingConverter.ConvertColumnName(sourceColumnName);
    }

    /// <summary>
    /// Apply column naming rules from source type configuration
    /// </summary>
    private void ApplyColumnNamingRules(ColumnDefinition column)
    {
        if (_sourceTypeConfig?.ColumnNaming == null)
        {
            return;
        }

        // Use group's rules if available, otherwise use default rules
        var rules = _currentNamingGroup?.Rules?.Count > 0
            ? _currentNamingGroup.Rules
            : _sourceTypeConfig.ColumnNaming.Rules;

        if (rules == null || rules.Count == 0)
        {
            return;
        }

        // Apply naming rules (replacement, is_hidden, description)
        foreach (var rule in rules)
        {
            if (Regex.IsMatch(column.SourceColumn ?? column.Name, rule.Pattern, RegexOptions.IgnoreCase))
            {
                // Apply replacement if specified
                if (!string.IsNullOrEmpty(rule.Replacement))
                {
                    column.Name = Regex.Replace(column.SourceColumn ?? column.Name, rule.Pattern, rule.Replacement, RegexOptions.IgnoreCase);
                }

                // Apply is_hidden if specified
                if (rule.IsHidden.HasValue)
                {
                    column.IsHidden = rule.IsHidden.Value;
                }

                // Apply description if specified
                if (!string.IsNullOrEmpty(rule.Description))
                {
                    column.Description = rule.Description;
                }

                // Only apply first matching rule
                break;
            }
        }
    }

    /// <summary>
    /// Filter columns based on exclude/include patterns from source config.
    /// Exclude is evaluated first, then include (if specified).
    /// Group-level patterns override default patterns.
    /// </summary>
    private List<CsvSchemaRow> FilterColumns(List<CsvSchemaRow> rows)
    {
        // Resolve patterns: group overrides default
        var excludePatterns = _currentNamingGroup?.ExcludePatterns?.Count > 0
            ? _currentNamingGroup.ExcludePatterns
            : _sourceTypeConfig?.ColumnNaming?.ExcludePatterns;

        var includePatterns = _currentNamingGroup?.IncludePatterns?.Count > 0
            ? _currentNamingGroup.IncludePatterns
            : _sourceTypeConfig?.ColumnNaming?.IncludePatterns;

        if ((excludePatterns == null || excludePatterns.Count == 0) &&
            (includePatterns == null || includePatterns.Count == 0))
        {
            return rows;
        }

        return rows.Where(row =>
        {
            var colName = row.ColumnName;

            // Exclude first
            if (excludePatterns?.Count > 0)
            {
                foreach (var pattern in excludePatterns)
                {
                    if (Regex.IsMatch(colName, pattern, RegexOptions.IgnoreCase))
                        return false;
                }
            }

            // Include filter (if specified, column must match at least one)
            if (includePatterns?.Count > 0)
            {
                foreach (var pattern in includePatterns)
                {
                    if (Regex.IsMatch(colName, pattern, RegexOptions.IgnoreCase))
                        return true;
                }
                return false; // No include pattern matched
            }

            return true;
        }).ToList();
    }

    /// <summary>
    /// Generate GeneratedTable metadata for manifest tracking
    /// </summary>
    public GeneratedTable GenerateMetadata(string tableName, TableDefinition tableDef, string filePath)
    {
        return new GeneratedTable
        {
            TableName = tableDef.Name,
            FilePath = filePath,
            ColumnsGenerated = tableDef.Columns.Select(c => c.Name).ToList(),
            ColumnTypes = tableDef.Columns.ToDictionary(c => c.Name, c => c.Type),
            SourceSchema = tableDef.Source?.Schema,
            SourceTable = tableDef.Source?.Table
        };
    }
}
