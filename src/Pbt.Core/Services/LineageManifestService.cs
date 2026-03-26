using System.Security.Cryptography;
using System.Text;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Service for managing lineage tag manifests
/// </summary>
public sealed class LineageManifestService
{
    private readonly YamlSerializer _serializer;
    private LineageManifest _manifest;
    private readonly HashSet<string> _newTags = new();
    private readonly List<string> _collisionWarnings = new();

    public LineageManifestService(YamlSerializer serializer)
    {
        _serializer = serializer;
        _manifest = new LineageManifest();
    }

    /// <summary>
    /// Load manifest from file, or create empty if doesn't exist
    /// </summary>
    public void LoadManifest(string projectPath)
    {
        var manifestPath = GetManifestPath(projectPath);

        if (File.Exists(manifestPath))
        {
            try
            {
                _manifest = _serializer.LoadFromFile<LineageManifest>(manifestPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load lineage manifest from {manifestPath}", ex);
            }
        }
        else
        {
            // Create new manifest
            _manifest = new LineageManifest
            {
                Version = 1,
                GeneratedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Save manifest to file
    /// </summary>
    public void SaveManifest(string projectPath)
    {
        var manifestPath = GetManifestPath(projectPath);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Update timestamp
        _manifest.GeneratedAt = DateTime.UtcNow;

        _serializer.SaveToFile(_manifest, manifestPath);
    }

    /// <summary>
    /// Get lineage tag for a table.
    /// The manifest is the sole source of truth. Deterministic hashing only applies on first creation.
    /// If a new object's deterministic hash collides with an orphaned manifest entry, a warning is emitted.
    /// </summary>
    public string GetOrGenerateTableTag(string tableName)
    {
        if (!_manifest.Tables.TryGetValue(tableName, out var tableLineage))
        {
            tableLineage = new TableLineage();
            _manifest.Tables[tableName] = tableLineage;
        }

        if (string.IsNullOrWhiteSpace(tableLineage.Self))
        {
            var candidateTag = GenerateDeterministicTag(tableName, tableName, "Table");
            CheckForCollision(candidateTag, $"Table: {tableName}");
            tableLineage.Self = candidateTag;
            _newTags.Add($"Table: {tableName}");
        }

        return tableLineage.Self;
    }

    /// <summary>
    /// Get lineage tag for a column
    /// </summary>
    public string GetOrGenerateColumnTag(string tableName, string columnName)
    {
        if (!_manifest.Tables.TryGetValue(tableName, out var tableLineage))
        {
            tableLineage = new TableLineage();
            _manifest.Tables[tableName] = tableLineage;
        }

        if (!tableLineage.Columns.TryGetValue(columnName, out var tag))
        {
            var candidateTag = GenerateDeterministicTag(tableName, columnName, "Column");
            CheckForCollision(candidateTag, $"Column: {tableName}.{columnName}");
            tag = candidateTag;
            tableLineage.Columns[columnName] = tag;
            _newTags.Add($"Column: {tableName}.{columnName}");
        }

        return tag;
    }

    /// <summary>
    /// Get lineage tag for a measure
    /// </summary>
    public string GetOrGenerateMeasureTag(string tableName, string measureName)
    {
        if (!_manifest.Tables.TryGetValue(tableName, out var tableLineage))
        {
            tableLineage = new TableLineage();
            _manifest.Tables[tableName] = tableLineage;
        }

        if (!tableLineage.Measures.TryGetValue(measureName, out var tag))
        {
            var candidateTag = GenerateDeterministicTag(tableName, measureName, "Measure");
            CheckForCollision(candidateTag, $"Measure: {tableName}.[{measureName}]");
            tag = candidateTag;
            tableLineage.Measures[measureName] = tag;
            _newTags.Add($"Measure: {tableName}.[{measureName}]");
        }

        return tag;
    }

    /// <summary>
    /// Get lineage tag for a hierarchy
    /// </summary>
    public string GetOrGenerateHierarchyTag(string tableName, string hierarchyName)
    {
        if (!_manifest.Tables.TryGetValue(tableName, out var tableLineage))
        {
            tableLineage = new TableLineage();
            _manifest.Tables[tableName] = tableLineage;
        }

        if (!tableLineage.Hierarchies.TryGetValue(hierarchyName, out var tag))
        {
            var candidateTag = GenerateDeterministicTag(tableName, hierarchyName, "Hierarchy");
            CheckForCollision(candidateTag, $"Hierarchy: {tableName}.{hierarchyName}");
            tag = candidateTag;
            tableLineage.Hierarchies[hierarchyName] = tag;
            _newTags.Add($"Hierarchy: {tableName}.{hierarchyName}");
        }

        return tag;
    }

    /// <summary>
    /// Get or generate UUID for a relationship
    /// </summary>
    public string GetOrGenerateRelationshipTag(string relationshipKey)
    {
        if (!_manifest.Relationships.TryGetValue(relationshipKey, out var tag))
        {
            tag = GenerateDeterministicTag("Relationship", relationshipKey, "Relationship");
            _manifest.Relationships[relationshipKey] = tag;
            _newTags.Add($"Relationship: {relationshipKey}");
        }

        return tag;
    }

    /// <summary>
    /// Get count of new tags generated
    /// </summary>
    public int NewTagCount => _newTags.Count;

    /// <summary>
    /// Get count of existing tags preserved
    /// </summary>
    public int ExistingTagCount => CountExistingTags();

    /// <summary>
    /// Clear all lineage tags (for reset operation)
    /// </summary>
    public void Clear()
    {
        _manifest = new LineageManifest
        {
            Version = 1,
            GeneratedAt = DateTime.UtcNow
        };
        _newTags.Clear();
    }

    /// <summary>
    /// Get all tables in manifest
    /// </summary>
    public IEnumerable<string> GetTables()
    {
        return _manifest.Tables.Keys;
    }

    /// <summary>
    /// Remove tags for objects that no longer exist in the project
    /// </summary>
    public int CleanOrphanedTags(TableRegistry tableRegistry, IEnumerable<ModelDefinition> models)
    {
        var removedCount = 0;
        var tablesToRemove = new List<string>();

        foreach (var tableName in _manifest.Tables.Keys.ToList())
        {
            // Check if table still exists in registry
            if (!tableRegistry.ContainsTable(tableName))
            {
                tablesToRemove.Add(tableName);
                continue;
            }

            var tableDef = tableRegistry.GetTable(tableName);
            var tableLineage = _manifest.Tables[tableName];

            // Clean orphaned columns
            var columnsToRemove = tableLineage.Columns.Keys
                .Where(colName => !tableDef.Columns.Any(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var col in columnsToRemove)
            {
                tableLineage.Columns.Remove(col);
                removedCount++;
            }

            // Clean orphaned hierarchies
            var hierarchiesToRemove = tableLineage.Hierarchies.Keys
                .Where(hierName => !tableDef.Hierarchies.Any(h => h.Name.Equals(hierName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var hier in hierarchiesToRemove)
            {
                tableLineage.Hierarchies.Remove(hier);
                removedCount++;
            }

            // Clean orphaned measures (check if they're in any model)
            var allMeasures = models.SelectMany(m => m.Measures)
                .Where(m => m.Table.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var measuresToRemove = tableLineage.Measures.Keys
                .Where(measureName => !allMeasures.Contains(measureName))
                .ToList();

            foreach (var measure in measuresToRemove)
            {
                tableLineage.Measures.Remove(measure);
                removedCount++;
            }
        }

        // Remove entire table entries that no longer exist
        foreach (var tableName in tablesToRemove)
        {
            _manifest.Tables.Remove(tableName);
            removedCount++;
        }

        return removedCount;
    }

    /// <summary>
    /// Get collision warnings generated during tag creation
    /// </summary>
    public IReadOnlyList<string> CollisionWarnings => _collisionWarnings;

    /// <summary>
    /// Check if a candidate tag collides with an existing tag in the manifest for a different object.
    /// This detects cases where renaming a column and creating a new one with the old name
    /// would produce a silent collision with an orphaned manifest entry.
    /// </summary>
    private void CheckForCollision(string candidateTag, string newObjectDescription)
    {
        foreach (var (tableName, tableLineage) in _manifest.Tables)
        {
            if (tableLineage.Self == candidateTag)
            {
                _collisionWarnings.Add(
                    $"Warning: New object '{newObjectDescription}' has a deterministic tag that collides with existing table '{tableName}'. " +
                    "This may indicate a renamed object. Consider running 'pbt lineage clean' first.");
                return;
            }

            foreach (var (colName, colTag) in tableLineage.Columns)
            {
                if (colTag == candidateTag)
                {
                    _collisionWarnings.Add(
                        $"Warning: New object '{newObjectDescription}' has a deterministic tag that collides with existing column '{tableName}.{colName}'. " +
                        "This may indicate a renamed object. Consider running 'pbt lineage clean' first.");
                    return;
                }
            }

            foreach (var (measureName, measureTag) in tableLineage.Measures)
            {
                if (measureTag == candidateTag)
                {
                    _collisionWarnings.Add(
                        $"Warning: New object '{newObjectDescription}' has a deterministic tag that collides with existing measure '{tableName}.[{measureName}]'. " +
                        "This may indicate a renamed object. Consider running 'pbt lineage clean' first.");
                    return;
                }
            }

            foreach (var (hierName, hierTag) in tableLineage.Hierarchies)
            {
                if (hierTag == candidateTag)
                {
                    _collisionWarnings.Add(
                        $"Warning: New object '{newObjectDescription}' has a deterministic tag that collides with existing hierarchy '{tableName}.{hierName}'. " +
                        "This may indicate a renamed object. Consider running 'pbt lineage clean' first.");
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Generate deterministic lineage tag using MD5 hash
    /// </summary>
    private string GenerateDeterministicTag(string tableName, string objectName, string objectType)
    {
        var seed = $"{tableName}_{objectName}_{objectType}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash).ToString();
    }

    /// <summary>
    /// Get the manifest file path
    /// </summary>
    private string GetManifestPath(string projectPath)
    {
        return Path.Combine(projectPath, ".pbt", "lineage.yaml");
    }

    /// <summary>
    /// Count existing (pre-existing, not newly generated) tags in the manifest
    /// </summary>
    private int CountExistingTags()
    {
        var totalCount = 0;
        foreach (var table in _manifest.Tables.Values)
        {
            if (!string.IsNullOrWhiteSpace(table.Self))
                totalCount++;
            totalCount += table.Columns.Count;
            totalCount += table.Measures.Count;
            totalCount += table.Hierarchies.Count;
        }
        totalCount += _manifest.Relationships.Count;
        return Math.Max(0, totalCount - _newTags.Count);
    }
}
