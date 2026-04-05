using YamlDotNet.Serialization;

namespace Pbt.Core.Models;

/// <summary>
/// Build output configuration
/// </summary>
public sealed class BuildConfig
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
