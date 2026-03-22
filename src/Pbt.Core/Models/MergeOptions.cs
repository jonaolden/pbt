namespace Pbt.Core.Models;

/// <summary>
/// Options controlling smart merge behavior
/// </summary>
public class MergeOptions
{
    /// <summary>
    /// If true, remove columns that exist in YAML but not in CSV
    /// Default: false (keep deleted columns for safety)
    /// </summary>
    public bool PruneDeleted { get; set; } = false;

    /// <summary>
    /// If true, show preview of changes without writing files
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// If true, update column types from CSV even if manually changed in YAML
    /// Default: true (CSV is source of truth for types)
    /// </summary>
    public bool UpdateTypes { get; set; } = true;

    /// <summary>
    /// If true, overwrite manual descriptions with CSV comments
    /// Default: false (preserve manual edits)
    /// </summary>
    public bool OverwriteDescriptions { get; set; } = false;

    /// <summary>
    /// Path to custom configuration file
    /// If null, uses built-in defaults
    /// </summary>
    public string? ConfigPath { get; set; }
}
