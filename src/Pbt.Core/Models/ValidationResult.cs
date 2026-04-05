namespace Pbt.Core.Models;

/// <summary>
/// Result of validation containing errors and warnings
/// </summary>
public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = new();
    private readonly List<ValidationError> _warnings = new();

    /// <summary>
    /// All validation errors
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => _errors;

    /// <summary>
    /// All validation warnings
    /// </summary>
    public IReadOnlyList<ValidationError> Warnings => _warnings;

    /// <summary>
    /// Whether validation passed (no errors)
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Whether there are any warnings
    /// </summary>
    public bool HasWarnings => _warnings.Count > 0;

    /// <summary>
    /// Total count of errors and warnings
    /// </summary>
    public int TotalIssues => _errors.Count + _warnings.Count;

    /// <summary>
    /// Add an error
    /// </summary>
    public void AddError(string message, string? filePath = null, string? context = null, string? suggestion = null)
    {
        _errors.Add(new ValidationError(ValidationSeverity.Error, message, filePath, context, suggestion));
    }

    /// <summary>
    /// Add a warning
    /// </summary>
    public void AddWarning(string message, string? filePath = null, string? context = null, string? suggestion = null)
    {
        _warnings.Add(new ValidationError(ValidationSeverity.Warning, message, filePath, context, suggestion));
    }

    /// <summary>
    /// Merge all errors and warnings from another result into this one
    /// </summary>
    public void Merge(ValidationResult other)
    {
        _errors.AddRange(other.Errors);
        _warnings.AddRange(other.Warnings);
    }

    /// <summary>
    /// Format all errors and warnings as a readable string
    /// </summary>
    public string FormatMessages()
    {
        if (IsValid && !HasWarnings)
        {
            return "✓ Validation passed with no issues";
        }

        var lines = new List<string>();

        if (_errors.Count > 0)
        {
            lines.Add($"Validation failed with {_errors.Count} error(s):");
            lines.Add("");
            foreach (var error in _errors)
            {
                lines.Add(error.Format());
            }
        }

        if (_warnings.Count > 0)
        {
            if (_errors.Count > 0)
            {
                lines.Add("");
            }
            lines.Add($"Warnings ({_warnings.Count}):");
            lines.Add("");
            foreach (var warning in _warnings)
            {
                lines.Add(warning.Format());
            }
        }

        return string.Join("\n", lines);
    }
}

/// <summary>
/// A single validation error or warning
/// </summary>
public sealed class ValidationError
{
    public ValidationSeverity Severity { get; }
    public string Message { get; }
    public string? FilePath { get; }
    public string? Context { get; }
    public string? Suggestion { get; }

    public ValidationError(ValidationSeverity severity, string message, string? filePath = null, string? context = null, string? suggestion = null)
    {
        Severity = severity;
        Message = message;
        FilePath = filePath;
        Context = context;
        Suggestion = suggestion;
    }

    public string Format()
    {
        var lines = new List<string>();

        var prefix = Severity == ValidationSeverity.Error ? "  ✗" : "  ⚠";

        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            lines.Add($"{prefix} {FilePath}");
            lines.Add($"    {Message}");
        }
        else
        {
            lines.Add($"{prefix} {Message}");
        }

        if (!string.IsNullOrWhiteSpace(Context))
        {
            lines.Add($"    Context: {Context}");
        }

        if (!string.IsNullOrWhiteSpace(Suggestion))
        {
            lines.Add($"    Suggestion: {Suggestion}");
        }

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Severity of a validation issue
/// </summary>
public enum ValidationSeverity
{
    Warning,
    Error
}
