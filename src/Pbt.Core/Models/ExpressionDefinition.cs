namespace Pbt.Core.Models;

/// <summary>
/// Represents a shared expression / Power Query parameter.
/// These emit as TMDL expression objects and can be referenced in table M expressions.
/// </summary>
public class ExpressionDefinition
{
    /// <summary>
    /// Expression name (e.g., "ServerName", "DatabaseName")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Kind of expression: Text, Int, Bool, etc.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// The M expression value
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Description of the expression
    /// </summary>
    public string? Description { get; set; }
}
