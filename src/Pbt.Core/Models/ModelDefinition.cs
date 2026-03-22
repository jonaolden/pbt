namespace Pbt.Core.Models;

/// <summary>
/// Represents a model composition (from models/*.yaml)
/// </summary>
public class ModelDefinition
{
    /// <summary>
    /// Model name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Model description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tables included in this model (references to table registry)
    /// </summary>
    public List<TableReference> Tables { get; set; } = new();

    /// <summary>
    /// Relationships between tables.
    /// Supports both verbose object syntax and shorthand string syntax:
    ///   Shorthand: "Sales.CustomerID -> Customers.CustomerID" (defaults to ManyToOne, Single, Active)
    ///   Verbose: from_table/from_column/to_table/to_column with optional overrides
    /// </summary>
    public List<RelationshipDefinition> Relationships { get; set; } = new();

    /// <summary>
    /// Measures defined in this model.
    /// Model-level measures override table-level measures with the same name.
    /// </summary>
    public List<MeasureDefinition> Measures { get; set; } = new();

    /// <summary>
    /// Shared expressions / Power Query parameters.
    /// These emit as TMDL expression objects for parameterized connections.
    /// </summary>
    public List<ExpressionDefinition>? Expressions { get; set; }

    /// <summary>
    /// Calculation groups for time intelligence, currency conversion, etc.
    /// </summary>
    public List<CalculationGroupDefinition>? CalculationGroups { get; set; }

    /// <summary>
    /// Perspectives that scope visibility for different report audiences
    /// </summary>
    public List<PerspectiveDefinition>? Perspectives { get; set; }

    /// <summary>
    /// Roles with row-level security (RLS) definitions
    /// </summary>
    public List<RoleDefinition>? Roles { get; set; }

    /// <summary>
    /// Field parameters for dynamic axis switching
    /// </summary>
    public List<FieldParameterDefinition>? FieldParameters { get; set; }

    /// <summary>
    /// File path where this model definition was loaded from
    /// </summary>
    public string? SourceFilePath { get; set; }
}
