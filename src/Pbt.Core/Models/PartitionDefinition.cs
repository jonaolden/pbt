namespace Pbt.Core.Models;

/// <summary>
/// Represents a partition in a table definition.
/// Supports multiple partitions for incremental refresh and mixed query modes.
/// </summary>
public class PartitionDefinition
{
    /// <summary>
    /// Partition name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Query mode: Import, DirectQuery, Dual
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// M expression (Power Query) for this partition
    /// </summary>
    public string? MExpression { get; set; }

    /// <summary>
    /// Path to external .m file containing the M expression
    /// </summary>
    public string? MExpressionFile { get; set; }
}
