using System.Text.RegularExpressions;

namespace Pbt.Core.Infrastructure;

/// <summary>
/// Loads .env files and resolves ${VAR} placeholders in configuration strings.
/// Follows standard .env conventions: KEY=VALUE, # comments, optional quoting.
/// Sets values as process environment variables so they're available globally.
/// </summary>
public static partial class EnvResolver
{
    private static readonly Regex PlaceholderPattern = PlaceholderRegex();

    /// <summary>
    /// Load a .env file and set values as environment variables.
    /// Does NOT overwrite existing environment variables (system env takes precedence).
    /// </summary>
    public static void LoadEnvFile(string envFilePath)
    {
        if (!File.Exists(envFilePath))
        {
            return; // Silently skip if no .env file
        }

        foreach (var line in File.ReadAllLines(envFilePath))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Parse KEY=VALUE (handle optional quoting)
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0)
            {
                continue;
            }

            var key = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();

            // Strip surrounding quotes (single or double)
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            // Only set if not already defined (system env takes precedence)
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>
    /// Resolve all ${VAR} placeholders in a string using environment variables.
    /// Throws if a referenced variable is not defined.
    /// </summary>
    public static string Resolve(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        return PlaceholderPattern.Replace(input, match =>
        {
            var varName = match.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(varName);

            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{varName}' is not defined. " +
                    $"Add it to your .env file or set it in your shell.");
            }

            return value;
        });
    }

    /// <summary>
    /// Try to resolve placeholders, returning false if any are missing.
    /// </summary>
    public static bool TryResolve(string? input, out string resolved)
    {
        try
        {
            resolved = Resolve(input);
            return true;
        }
        catch (InvalidOperationException)
        {
            resolved = input ?? string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Check if a string contains unresolved ${VAR} placeholders.
    /// </summary>
    public static bool HasPlaceholders(string? input)
    {
        return !string.IsNullOrEmpty(input) && PlaceholderPattern.IsMatch(input);
    }

    /// <summary>
    /// Find the .env file by walking up from the given directory.
    /// Checks the given directory first, then parent directories up to the filesystem root.
    /// </summary>
    public static string? FindEnvFile(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);

        while (dir != null)
        {
            var envPath = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envPath))
            {
                return envPath;
            }

            dir = dir.Parent;
        }

        return null;
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderRegex();
}
