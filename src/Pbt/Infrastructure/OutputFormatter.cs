using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pbt.Infrastructure;

/// <summary>
/// Global output formatting support for --output json and --ci modes
/// </summary>
public static class OutputFormatter
{
    /// <summary>
    /// Whether JSON output mode is active
    /// </summary>
    public static bool JsonMode { get; set; }

    /// <summary>
    /// Whether CI mode is active (no color, non-interactive, strict exit codes)
    /// </summary>
    public static bool CiMode { get; set; }

    /// <summary>
    /// Shared JSON serializer options
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Write colored text (respects CI mode)
    /// </summary>
    public static void WriteColored(string text, ConsoleColor color)
    {
        if (CiMode)
        {
            Console.WriteLine(text);
        }
        else
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Output a result as JSON or text depending on mode
    /// </summary>
    public static void WriteResult(object jsonResult, string textOutput)
    {
        if (JsonMode)
        {
            Console.WriteLine(JsonSerializer.Serialize(jsonResult, JsonOptions));
        }
        else
        {
            Console.WriteLine(textOutput);
        }
    }
}
