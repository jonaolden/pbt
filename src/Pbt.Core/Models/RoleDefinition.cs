namespace Pbt.Core.Models;

/// <summary>
/// Represents a role with row-level security (RLS) definitions
/// </summary>
public class RoleDefinition
{
    /// <summary>
    /// Role name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Model permission: Read, ReadRefresh, None
    /// </summary>
    public string ModelPermission { get; set; } = "Read";

    /// <summary>
    /// Table-level filter permissions for RLS
    /// </summary>
    public List<TablePermissionDefinition> TablePermissions { get; set; } = new();
}

/// <summary>
/// Represents a table-level permission filter for RLS
/// </summary>
public class TablePermissionDefinition
{
    /// <summary>
    /// Table name to apply the filter to
    /// </summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// DAX filter expression for row-level security
    /// </summary>
    public string FilterExpression { get; set; } = string.Empty;
}
