namespace Pbt.Core.Models;

/// <summary>
/// Represents a calculation group definition.
/// Calculation groups are technically tables with a special structure
/// containing calculation items for time intelligence, currency conversion, etc.
/// </summary>
public class CalculationGroupDefinition
{
    /// <summary>
    /// Calculation group name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The attribute column for the calculation group
    /// </summary>
    public List<ColumnDefinition> Columns { get; set; } = new();

    /// <summary>
    /// Calculation items in this group
    /// </summary>
    public List<CalculationItemDefinition> CalculationItems { get; set; } = new();

    /// <summary>
    /// Precedence for calculation group evaluation order
    /// </summary>
    public int? Precedence { get; set; }

    /// <summary>
    /// File path where this definition was loaded from
    /// </summary>
    public string? SourceFilePath { get; set; }
}

/// <summary>
/// Represents a single calculation item within a calculation group
/// </summary>
public class CalculationItemDefinition
{
    /// <summary>
    /// Calculation item name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// DAX expression for the calculation item
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Optional format string expression (DAX)
    /// </summary>
    public string? FormatStringExpression { get; set; }

    /// <summary>
    /// Display ordinal for ordering items
    /// </summary>
    public int? Ordinal { get; set; }

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }
}
