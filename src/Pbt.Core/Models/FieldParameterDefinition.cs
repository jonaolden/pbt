namespace Pbt.Core.Models;

/// <summary>
/// Represents a field parameter for dynamic axis switching in reports.
/// Field parameters are structurally tables with a specific annotation.
/// </summary>
public class FieldParameterDefinition
{
    /// <summary>
    /// Field parameter name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The fields/measures included in this parameter
    /// </summary>
    public List<FieldParameterValue> Values { get; set; } = new();

    /// <summary>
    /// File path where this definition was loaded from
    /// </summary>
    public string? SourceFilePath { get; set; }
}

/// <summary>
/// A single value in a field parameter
/// </summary>
public class FieldParameterValue
{
    /// <summary>
    /// Display name for this value
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// DAX expression reference (e.g., a measure reference or column reference)
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Display ordinal
    /// </summary>
    public int? Ordinal { get; set; }
}
