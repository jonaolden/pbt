namespace Pbt.Core.Models;

/// <summary>
/// Represents a reference to a table in the registry
/// </summary>
public class TableReference
{
    /// <summary>
    /// Name of the table to reference from the registry
    /// </summary>
    public string Ref { get; set; } = string.Empty;
}
