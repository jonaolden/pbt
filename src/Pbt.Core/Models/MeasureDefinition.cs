namespace Pbt.Core.Models;

/// <summary>
/// Represents a DAX measure
/// </summary>
public class MeasureDefinition
{
    /// <summary>
    /// Measure name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Table where this measure will be added
    /// </summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// DAX expression
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Format string for display
    /// </summary>
    public string? FormatString { get; set; }

    /// <summary>
    /// Measure description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Display folder for organizing measures
    /// </summary>
    public string? DisplayFolder { get; set; }

    /// <summary>
    /// Whether the measure is hidden
    /// </summary>
    public bool? IsHidden { get; set; }

    /// <summary>
    /// Lineage tag (optional override, usually auto-generated)
    /// </summary>
    public string? LineageTag { get; set; }
}
