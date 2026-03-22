using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Registry for table definitions loaded from configured asset paths
/// </summary>
public class TableRegistry
{
    private readonly Dictionary<string, TableDefinition> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tableSourceGroup = new(StringComparer.OrdinalIgnoreCase);
    private readonly YamlSerializer _serializer;

    public TableRegistry(YamlSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Number of tables loaded in the registry
    /// </summary>
    public int Count => _tables.Count;

    /// <summary>
    /// Load table definitions from multiple paths with priority ordering
    /// Tables from earlier paths (higher priority) override tables from later paths
    /// </summary>
    /// <param name="tablePaths">List of table directory paths, ordered by priority (first = highest)</param>
    public void LoadTablesWithPriority(List<string> tablePaths)
    {
        if (tablePaths == null || tablePaths.Count == 0)
        {
            throw new ArgumentException("At least one table path must be provided", nameof(tablePaths));
        }

        // Process paths in order - first path has highest priority
        for (int i = 0; i < tablePaths.Count; i++)
        {
            var tablesPath = tablePaths[i];
            var priority = i; // Lower index = higher priority

            if (!Directory.Exists(tablesPath))
            {
                throw new DirectoryNotFoundException($"Tables directory not found: {tablesPath}");
            }

            var yamlFiles = Directory.GetFiles(tablesPath, "*.yaml", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(tablesPath, "*.yml", SearchOption.TopDirectoryOnly))
                .ToList();

            foreach (var file in yamlFiles)
            {
                try
                {
                    var table = _serializer.LoadFromFile<TableDefinition>(file);

                    if (string.IsNullOrWhiteSpace(table.Name))
                    {
                        throw new InvalidOperationException($"Table definition in {file} has no name");
                    }

                    table.SourceFilePath = file;

                    // Higher priority (lower index) tables win - skip if already loaded
                    if (!_tables.ContainsKey(table.Name))
                    {
                        _tables[table.Name] = table;
                        _tableSourceGroup[table.Name] = tablesPath;
                    }
                    // Note: We silently skip lower priority duplicates (this is expected behavior)
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to load table from {file}: {ex.Message}", ex);
                }
            }
        }

        if (_tables.Count == 0)
        {
            throw new InvalidOperationException(
                $"No table definitions found in any configured paths: {string.Join(", ", tablePaths)}");
        }
    }

    /// <summary>
    /// Load all table definitions from a single directory (legacy method)
    /// </summary>
    /// <param name="tablesPath">Path to the tables/ directory</param>
    /// <exception cref="DirectoryNotFoundException">If the tables directory doesn't exist</exception>
    /// <exception cref="InvalidOperationException">If duplicate table names are found</exception>
    public void LoadTables(string tablesPath)
    {
        if (!Directory.Exists(tablesPath))
        {
            throw new DirectoryNotFoundException($"Tables directory not found: {tablesPath}");
        }

        var yamlFiles = Directory.GetFiles(tablesPath, "*.yaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(tablesPath, "*.yml", SearchOption.TopDirectoryOnly))
            .ToList();

        if (yamlFiles.Count == 0)
        {
            throw new InvalidOperationException($"No YAML files found in tables directory: {tablesPath}");
        }

        var duplicates = new List<string>();

        foreach (var file in yamlFiles)
        {
            try
            {
                var table = _serializer.LoadFromFile<TableDefinition>(file);

                // Validate table has a name
                if (string.IsNullOrWhiteSpace(table.Name))
                {
                    throw new InvalidOperationException($"Table definition in {file} has no name");
                }

                // Store source file path
                table.SourceFilePath = file;

                // Check for duplicates
                if (_tables.ContainsKey(table.Name))
                {
                    duplicates.Add($"Table '{table.Name}' defined in both '{_tables[table.Name].SourceFilePath}' and '{file}'");
                }
                else
                {
                    _tables[table.Name] = table;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load table from {file}: {ex.Message}", ex);
            }
        }

        if (duplicates.Any())
        {
            throw new InvalidOperationException($"Duplicate table names found:\n  " + string.Join("\n  ", duplicates));
        }
    }

    /// <summary>
    /// Get the source group (path) where a table was loaded from
    /// </summary>
    /// <param name="tableName">Table name</param>
    /// <returns>Source path or null if not tracked</returns>
    public string? GetTableSourceGroup(string tableName)
    {
        return _tableSourceGroup.TryGetValue(tableName, out var group) ? group : null;
    }

    /// <summary>
    /// Get a table definition by name
    /// </summary>
    /// <param name="name">Table name (case-insensitive)</param>
    /// <returns>Table definition</returns>
    /// <exception cref="KeyNotFoundException">If table is not found</exception>
    public TableDefinition GetTable(string name)
    {
        if (!_tables.TryGetValue(name, out var table))
        {
            var availableTables = string.Join(", ", _tables.Keys);
            throw new KeyNotFoundException(
                $"Table '{name}' not found in registry. Available tables: {availableTables}");
        }

        return table;
    }

    /// <summary>
    /// Try to get a table definition by name
    /// </summary>
    /// <param name="name">Table name (case-insensitive)</param>
    /// <param name="table">Output table definition if found</param>
    /// <returns>True if table was found</returns>
    public bool TryGetTable(string name, out TableDefinition? table)
    {
        return _tables.TryGetValue(name, out table);
    }

    /// <summary>
    /// Check if a table exists in the registry
    /// </summary>
    /// <param name="name">Table name (case-insensitive)</param>
    /// <returns>True if table exists</returns>
    public bool ContainsTable(string name)
    {
        return _tables.ContainsKey(name);
    }

    /// <summary>
    /// List all table names in the registry
    /// </summary>
    /// <returns>List of table names</returns>
    public List<string> ListTables()
    {
        return _tables.Keys.ToList();
    }

    /// <summary>
    /// Get all table definitions
    /// </summary>
    /// <returns>Collection of all table definitions</returns>
    public IEnumerable<TableDefinition> GetAllTables()
    {
        return _tables.Values;
    }

    /// <summary>
    /// Clear all loaded tables
    /// </summary>
    public void Clear()
    {
        _tables.Clear();
    }
}
