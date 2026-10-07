using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Source-specific part of table generation. M rendering is not here: the returned
/// <see cref="SourceDefinition"/> is expanded to M by <c>ModelComposer</c> at build time,
/// and source-to-TMDL type mapping is owned by the metadata provider (the Python plugins' types.py).
/// </summary>
public interface ISourceAdapter
{
    /// <summary>Returns an actionable error message, or null if the target is valid.</summary>
    string? ValidateTarget(string target);

    /// <summary>Columns (source order) and description from the live source. Throws on lookup failure.</summary>
    TableDefinition GetTableMetadata(string target);

    SourceDefinition ToSource(string target, SourceConnectionConfig connection);
}

public static partial class SnowflakeTarget
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_$]*$")]
    private static partial Regex Plain();

    /// <summary>
    /// Parse <c>database.schema.table</c>. Unquoted parts fold to upper case (Snowflake rules);
    /// <c>"quoted"</c> parts keep case, with <c>""</c> as an escaped quote.
    /// </summary>
    public static (string Database, string Schema, string Table) Parse(string target)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false, quoted = false;

        void EndPart()
        {
            var raw = sb.ToString();
            if (raw.Length == 0) throw new ArgumentException($"Empty identifier in '{target}'.");
            if (!quoted && !Plain().IsMatch(raw))
                throw new ArgumentException($"Identifier '{raw}' in '{target}' needs double quotes (e.g. \"{raw}\").");
            if (quoted && raw.Contains('.'))
                throw new ArgumentException($"Identifier '{raw}' contains '.', which is not supported.");
            parts.Add(quoted ? raw : raw.ToUpperInvariant());
            sb.Clear();
            quoted = false;
        }

        for (var i = 0; i < target.Length; i++)
        {
            var c = target[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < target.Length && target[i + 1] == '"') { sb.Append('"'); i++; }
                else { inQuotes = !inQuotes; quoted = true; }
            }
            else if (c == '.' && !inQuotes) EndPart();
            else sb.Append(c);
        }
        if (inQuotes) throw new ArgumentException($"Unterminated quote in '{target}'.");
        EndPart();

        if (parts.Count != 3)
            throw new ArgumentException($"Snowflake target '{target}' has {parts.Count} component(s); expected database.schema.table.");
        return (parts[0], parts[1], parts[2]);
    }
}

/// <summary>
/// Snowflake adapter: metadata comes from <c>python -m snowflake_to_tmdl</c> (credentials read from its env vars,
/// never passed or logged here); its TMDL output is imported with <see cref="TmdlTableImporter"/>.
/// </summary>
public sealed class SnowflakeAdapter : ISourceAdapter
{
    private readonly Func<string, string, (int ExitCode, string Error)> _runPlugin;

    /// <param name="runPlugin">(refText, outputFile) -> exit code + stderr; overridable for tests</param>
    public SnowflakeAdapter(Func<string, string, (int, string)>? runPlugin = null)
    {
        _runPlugin = runPlugin ?? RunPythonPlugin;
    }

    public string? ValidateTarget(string target)
    {
        try { SnowflakeTarget.Parse(target); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    public TableDefinition GetTableMetadata(string target)
    {
        var (db, schema, table) = SnowflakeTarget.Parse(target);
        var file = Path.Combine(Path.GetTempPath(), $"pbt_sf_{Guid.NewGuid():N}.tmdl");
        try
        {
            (int code, string err) result;
            try { result = _runPlugin($"{db}.{schema}.{table}", file); }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not start Python ({ex.Message}). Install plugins/snowflake_to_tmdl or set PBT_PYTHON.");
            }
            if (result.code != 0 || !File.Exists(file))
                throw new InvalidOperationException($"snowflake_to_tmdl failed for {db}.{schema}.{table}: {result.err.Trim()}");

            return new TmdlTableImporter(new YamlSerializer()).ExtractTables(file).FirstOrDefault()
                ?? throw new InvalidOperationException($"No table found in metadata for {db}.{schema}.{table}.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public SourceDefinition ToSource(string target, SourceConnectionConfig connection)
    {
        var (db, schema, table) = SnowflakeTarget.Parse(target);
        return new SourceDefinition
        {
            Type = "snowflake",
            Connector = connection.Connector,
            Connection = connection.Connection,
            Database = db,
            Schema = schema,
            Table = table
        };
    }

    private static (int, string) RunPythonPlugin(string refText, string outFile)
    {
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("PBT_PYTHON") ?? "python3")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var a in new[] { "-m", "snowflake_to_tmdl", refText, "-o", outFile, "--no-partition" })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var err = p.StandardError.ReadToEnd();
        stdout.Wait();
        p.WaitForExit();
        return (p.ExitCode, err);
    }
}
