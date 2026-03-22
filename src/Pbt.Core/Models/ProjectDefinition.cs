using YamlDotNet.Serialization;

namespace Pbt.Core.Models;

/// <summary>
/// Represents the project-level configuration (project.yml)
/// </summary>
public class ProjectDefinition
{
    /// <summary>
    /// Project name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Project description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Power BI compatibility level (default 1600)
    /// </summary>
    public int CompatibilityLevel { get; set; } = 1600;

    /// <summary>
    /// Format strings for each type (applied during display)
    /// Maps type names (e.g., "int64", "decimal", "dateTime") to format strings
    /// </summary>
    public Dictionary<string, string?> FormatStrings { get; set; } = new();

    /// <summary>
    /// Assets configuration - ordered by priority (first = highest)
    /// Maps group names (e.g., "project", "common") to their asset paths
    /// </summary>
    public Dictionary<string, List<AssetPathConfig>>? Assets { get; set; }

    /// <summary>
    /// Build output configuration
    /// </summary>
    public BuildConfig? Builds { get; set; }

    /// <summary>
    /// Project-level shared expressions / Power Query parameters.
    /// Can be overridden per-environment using environments/*.env.yml files.
    /// </summary>
    public List<ExpressionDefinition>? Expressions { get; set; }
}

/// <summary>
/// Configuration for an asset path entry
/// Each entry can specify either a 'path' (for all asset types) or specific asset type paths
/// </summary>
public class AssetPathConfig
{
    /// <summary>
    /// Path containing all asset types in subdirectories (tables/, macros/, models/)
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Path to tables directory
    /// </summary>
    public string? Tables { get; set; }

    /// <summary>
    /// Path to macros directory
    /// </summary>
    public string? Macros { get; set; }

    /// <summary>
    /// Path to models directory
    /// </summary>
    public string? Models { get; set; }
}

/// <summary>
/// Build output configuration
/// </summary>
public class BuildConfig
{
    /// <summary>
    /// Path to write build output (absolute path)
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Whether to include subdirectories when writing output files
    /// </summary>
    [YamlMember(Alias = "include_subdirectories")]
    public bool IncludeSubdirectories { get; set; } = true;
}
