namespace Pbt.Core.Models;

/// <summary>
/// Represents a partition in a table definition.
/// Supports multiple partitions for incremental refresh and mixed query modes.
/// </summary>
public class PartitionDefinition : MetadataDefinition
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

    /// <summary>
    /// Partition description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// DAX expression (calculated table partition)
    /// </summary>
    public string? CalculatedExpression { get; set; }

    /// <summary>
    /// Native query in the data source's language (query partition, e.g. DirectQuery); needs data_source
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// Name of a model data_sources entry used by query partitions
    /// </summary>
    public string? DataSource { get; set; }

    /// <summary>
    /// Direct Lake entity (table/view) name; needs expression_source
    /// </summary>
    public string? EntityName { get; set; }

    /// <summary>
    /// Direct Lake entity schema
    /// </summary>
    public string? SchemaName { get; set; }

    /// <summary>
    /// Name of the shared expression (e.g. a Fabric connection) that Direct Lake entities come from
    /// </summary>
    public string? ExpressionSource { get; set; }
}
