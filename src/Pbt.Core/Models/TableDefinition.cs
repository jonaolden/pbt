namespace Pbt.Core.Models;

/// <summary>
/// Represents a table definition (from tables/*.yaml)
/// </summary>
public class TableDefinition
{
    /// <summary>
    /// Table name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Table description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// M expression (Power Query) for loading data.
    /// Shorthand that expands to a single default partition for simple cases.
    /// For multiple partitions (e.g., incremental refresh), use the Partitions list.
    /// </summary>
    public string? MExpression { get; set; }

    /// <summary>
    /// Path to external .m file containing the M expression.
    /// Alternative to inline MExpression for better readability and syntax highlighting.
    /// </summary>
    public string? MExpressionFile { get; set; }

    /// <summary>
    /// Multiple partitions for incremental refresh, mixed query modes, etc.
    /// When set, takes precedence over MExpression/MExpressionFile.
    /// </summary>
    public List<PartitionDefinition>? Partitions { get; set; }

    /// <summary>
    /// Lineage tag (optional override, usually auto-generated)
    /// </summary>
    public string? LineageTag { get; set; }

    /// <summary>
    /// Optional source metadata (alternative to MExpression)
    /// Used by PbiScaffold, can be converted to MExpression by PbiComposer at build time
    /// </summary>
    public SourceDefinition? Source { get; set; }

    /// <summary>
    /// Whether the table should be hidden from client tools
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Table columns
    /// </summary>
    public List<ColumnDefinition> Columns { get; set; } = new();

    /// <summary>
    /// Table hierarchies
    /// </summary>
    public List<HierarchyDefinition> Hierarchies { get; set; } = new();

    /// <summary>
    /// Table measures (DAX calculations)
    /// </summary>
    public List<MeasureDefinition> Measures { get; set; } = new();

    /// <summary>
    /// Key-value annotations for tooling metadata and extended properties
    /// </summary>
    public Dictionary<string, string>? Annotations { get; set; }

    /// <summary>
    /// File path where this table definition was loaded from
    /// </summary>
    public string? SourceFilePath { get; set; }
}
