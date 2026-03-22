namespace Pbt.Core.Models;

/// <summary>
/// Represents a relationship between two tables
/// </summary>
public class RelationshipDefinition
{
    /// <summary>
    /// Source table name (many side)
    /// </summary>
    public string FromTable { get; set; } = string.Empty;

    /// <summary>
    /// Source column name
    /// </summary>
    public string FromColumn { get; set; } = string.Empty;

    /// <summary>
    /// Target table name (one side)
    /// </summary>
    public string ToTable { get; set; } = string.Empty;

    /// <summary>
    /// Target column name
    /// </summary>
    public string ToColumn { get; set; } = string.Empty;

    /// <summary>
    /// Cardinality (ManyToOne, OneToMany, OneToOne, ManyToMany)
    /// </summary>
    public string Cardinality { get; set; } = "ManyToOne";

    /// <summary>
    /// Cross filter direction (Single, Both).
    /// The Tabular Object Model only supports Single and Both - None is not valid.
    /// </summary>
    public string? CrossFilterDirection { get; set; }

    /// <summary>
    /// Whether the relationship is active
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// Assume referential integrity for DirectQuery performance optimization.
    /// When true, the engine uses INNER JOIN instead of OUTER JOIN, improving query performance.
    /// Critical for DirectQuery models with large fact tables.
    /// </summary>
    public bool RelyOnReferentialIntegrity { get; set; }
}
