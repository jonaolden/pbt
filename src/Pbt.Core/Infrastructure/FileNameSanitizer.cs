namespace Pbt.Core.Infrastructure;

/// <summary>
/// Shared utility for sanitizing names for filesystem usage
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>
    /// Sanitize a name to be safe for filesystem usage.
    /// Replaces invalid filename characters and spaces with underscores.
    /// </summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Model";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        sanitized = sanitized.Replace(' ', '_');

        return string.IsNullOrWhiteSpace(sanitized) ? "Model" : sanitized;
    }

    /// <summary>
    /// Sanitize a name and convert to lowercase for use as a filename.
    /// </summary>
    public static string SanitizeToLower(string name)
    {
        return Sanitize(name).ToLowerInvariant();
    }
}
