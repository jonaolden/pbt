namespace Pbt.Core.Models;

/// <summary>
/// Represents a model composition (from models/*.yaml).
/// Also carries project-level configuration (compatibility level, format strings, asset paths, build output)
/// that was previously split into a separate project.yml file.
/// </summary>
public class ModelDefinition : MetadataDefinition
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
    /// Power BI compatibility level (default 1600)
    /// </summary>
    public int CompatibilityLevel { get; set; } = 1600;

    /// <summary>
    /// When true, Power BI discourages implicit measures (required for calculation groups).
    /// Defaults to true. Set to false in model YAML to override.
    /// </summary>
    public bool DiscourageImplicitMeasures { get; set; } = true;

    /// <summary>
    /// Culture/locale for the model (e.g., "en-US").
    /// Defaults to "en-US" if not specified.
    /// </summary>
    public string Culture { get; set; } = "en-US";

    /// <summary>
    /// Source query culture/locale (e.g., "en-SE").
    /// Controls how data source queries interpret locale-specific formats.
    /// Defaults to "en-US" if not specified.
    /// </summary>
    public string SourceQueryCulture { get; set; } = "en-US";

    /// <summary>
    /// When true, Power BI auto-generates time intelligence for date columns.
    /// Defaults to false (disabled) to avoid unwanted auto-generated date hierarchies.
    /// Set to true in model YAML to enable.
    /// </summary>
    public bool AutoTimeIntelligence { get; set; } = false;

    /// <summary>
    /// Format strings applied to columns by data type when no explicit format_string is set.
    /// Maps type names (e.g., "int64", "decimal", "dateTime") to format strings.
    /// </summary>
    public Dictionary<string, string?> FormatStrings { get; set; } = new();

    /// <summary>
    /// Assets configuration — ordered by priority (first = highest).
    /// Maps group names (e.g., "project", "common") to their asset paths.
    /// When omitted, the tool uses the convention-based layout (tables/ and macros/ next to the project root).
    /// </summary>
    public Dictionary<string, List<AssetPathConfig>>? Assets { get; set; }

    /// <summary>
    /// Build output configuration
    /// </summary>
    public BuildConfig? Builds { get; set; }

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
    /// Provider/structured data sources referenced by query partitions (DirectQuery)
    /// </summary>
    public List<DataSourceDefinition>? DataSources { get; set; }

    /// <summary>
    /// DAX user-defined functions (needs a recent compatibility level)
    /// </summary>
    public List<FunctionDefinition>? Functions { get; set; }

    /// <summary>
    /// Cultures with translated captions, descriptions and display folders
    /// </summary>
    public List<CultureDefinition>? Cultures { get; set; }

    /// <summary>
    /// Field parameters for dynamic axis switching
    /// </summary>
    public List<FieldParameterDefinition>? FieldParameters { get; set; }

    /// <summary>
    /// File path where this model definition was loaded from
    /// </summary>
    public string? SourceFilePath { get; set; }
}

/// <summary>
/// A model data source. Set connection_string (provider data source) or protocol + address (structured).
/// </summary>
public class DataSourceDefinition : MetadataDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Provider data source: connection string
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Provider data source: provider name (optional)
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Structured data source: protocol (e.g. "tds", "snowflake")
    /// </summary>
    public string? Protocol { get; set; }

    /// <summary>
    /// Structured data source: address keys (e.g. server, database)
    /// </summary>
    public Dictionary<string, string>? Address { get; set; }

    /// <summary>
    /// Structured data source: credential keys (e.g. AuthenticationKind, PrivacySetting)
    /// </summary>
    public Dictionary<string, string>? Credential { get; set; }
}

public class FunctionDefinition : MetadataDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsHidden { get; set; }
}

public class CultureDefinition : MetadataDefinition
{
    /// <summary>
    /// Culture name (e.g. "sv-SE")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public List<TranslationDefinition> Translations { get; set; } = new();
}

public class TranslationDefinition
{
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// Column, measure or hierarchy name in the table; omit to translate the table itself
    /// </summary>
    public string? Object { get; set; }

    /// <summary>
    /// Caption, Description or DisplayFolder
    /// </summary>
    public string Property { get; set; } = "Caption";

    public string Value { get; set; } = string.Empty;
}
