namespace Pbt.Core.Models;

/// <summary>
/// Represents a named environment configuration for promoting models between
/// dev/test/prod without editing expression values manually.
/// Stored in environments/*.env.yml files.
/// </summary>
public class EnvironmentDefinition
{
    /// <summary>
    /// Environment name (e.g., "dev", "prod")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Expression overrides: maps expression names to their environment-specific values.
    /// Values support ${ENV_VAR} substitution so secrets never appear in files.
    /// </summary>
    public Dictionary<string, string> Expressions { get; set; } = new();
}
