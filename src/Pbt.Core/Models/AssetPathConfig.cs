namespace Pbt.Core.Models;

/// <summary>
/// Configuration for an asset path entry
/// Each entry can specify either a 'path' (for all asset types) or specific asset type paths
/// </summary>
public sealed class AssetPathConfig
{
    /// <summary>
    /// Path containing all asset types in subdirectories (tables/, models/)
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Path to tables directory
    /// </summary>
    public string? Tables { get; set; }

    /// <summary>
    /// Path to models directory
    /// </summary>
    public string? Models { get; set; }

    /// <summary>
    /// Path to macros directory
    /// </summary>
    public string? Macros { get; set; }
}
