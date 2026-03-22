namespace Pbt.Core.Models;

/// <summary>
/// Represents a perspective definition that scopes visibility for report audiences
/// </summary>
public class PerspectiveDefinition
{
    /// <summary>
    /// Perspective name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tables included in this perspective
    /// </summary>
    public List<string> Tables { get; set; } = new();

    /// <summary>
    /// Specific measures included (if empty, all measures from included tables are visible)
    /// </summary>
    public List<string>? Measures { get; set; }

    /// <summary>
    /// Specific columns to exclude from included tables
    /// Format: "TableName.ColumnName"
    /// </summary>
    public List<string>? ExcludeColumns { get; set; }
}
