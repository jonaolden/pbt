namespace Pbt.Core.Models;

/// <summary>
/// Base for YAML definitions that map to TOM objects carrying annotations and extended properties
/// </summary>
public abstract class MetadataDefinition
{
    /// <summary>
    /// Key-value annotations for tooling metadata
    /// </summary>
    public Dictionary<string, string>? Annotations { get; set; }

    /// <summary>
    /// Key-value extended properties (string values)
    /// </summary>
    public Dictionary<string, string>? ExtendedProperties { get; set; }
}
