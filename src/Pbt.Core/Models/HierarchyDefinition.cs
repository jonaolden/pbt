namespace Pbt.Core.Models;

/// <summary>
/// Represents a hierarchy in a table
/// </summary>
public class HierarchyDefinition
{
    /// <summary>
    /// Hierarchy name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Hierarchy description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Display folder for organizing hierarchies in the field list
    /// </summary>
    public string? DisplayFolder { get; set; }

    /// <summary>
    /// Lineage tag (optional override, usually auto-generated)
    /// </summary>
    public string? LineageTag { get; set; }

    /// <summary>
    /// Hierarchy levels
    /// </summary>
    public List<LevelDefinition> Levels { get; set; } = new();
}

/// <summary>
/// Represents a level in a hierarchy
/// </summary>
public class LevelDefinition
{
    /// <summary>
    /// Level name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Column name for this level
    /// </summary>
    public string Column { get; set; } = string.Empty;
}
