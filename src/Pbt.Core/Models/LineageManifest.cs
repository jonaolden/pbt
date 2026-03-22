namespace Pbt.Core.Models;

/// <summary>
/// Manifest file that stores lineage tags for all objects in the model
/// Persisted to .pbt/lineage.yaml
/// </summary>
public class LineageManifest
{
    /// <summary>
    /// Manifest version for future compatibility
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Timestamp when the manifest was last generated
    /// </summary>
    public DateTime? GeneratedAt { get; set; }

    /// <summary>
    /// Table lineage tags
    /// </summary>
    public Dictionary<string, TableLineage> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Relationship lineage tags (relationship key -> lineage tag/ID)
    /// </summary>
    public Dictionary<string, string> Relationships { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Lineage tags for a single table and its objects
/// </summary>
public class TableLineage
{
    /// <summary>
    /// Lineage tag for the table itself
    /// </summary>
    public string? Self { get; set; }

    /// <summary>
    /// Column lineage tags (column name -> lineage tag)
    /// </summary>
    public Dictionary<string, string> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Measure lineage tags (measure name -> lineage tag)
    /// </summary>
    public Dictionary<string, string> Measures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Hierarchy lineage tags (hierarchy name -> lineage tag)
    /// </summary>
    public Dictionary<string, string> Hierarchies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
