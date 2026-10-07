using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

public sealed record TableGenResult(string Table, string Status, string? Error = null);

public sealed record GenerateReport(List<TableGenResult> Tables, List<string> RefsAdded)
{
    public bool HasErrors => Tables.Any(t => t.Status == "error");
}

/// <summary>
/// Expands <see cref="ProjectConfig"/> table specs into tables/*.yaml, merging into existing files
/// with <see cref="TableMerger"/> so manual measures, hierarchies and descriptions survive.
/// </summary>
public sealed class TableGenerator
{
    private readonly YamlSerializer _yaml = new();
    private readonly Dictionary<string, ISourceAdapter> _adapters;

    public TableGenerator(Dictionary<string, ISourceAdapter>? adapters = null)
    {
        _adapters = adapters ?? new(StringComparer.OrdinalIgnoreCase) { ["snowflake"] = new SnowflakeAdapter(), ["csv"] = new CsvAdapter() };
    }

    /// <summary>Offline validation. Returns one error (or null) per spec, same order as config.Tables.</summary>
    public List<string?> Validate(ProjectConfig config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return config.Tables.Select((t, i) => ValidateSpec(config, t, i, seen)).ToList();
    }

    private string? ValidateSpec(ProjectConfig config, TableGenSpec t, int i, HashSet<string> seen)
    {
        var at = $"tables[{i}]";
        if (string.IsNullOrWhiteSpace(t.TableName)) return $"{at}: table_name is required.";
        if (!seen.Add(t.TableName)) return $"{at} ({t.TableName}): duplicate table_name.";
        if (string.IsNullOrWhiteSpace(t.Source)) return $"{at} ({t.TableName}): source is required.";
        if (string.IsNullOrWhiteSpace(t.Target)) return $"{at} ({t.TableName}): target is required.";
        if (!config.Sources.TryGetValue(t.Source, out var conn) || !_adapters.TryGetValue(t.Source, out var adapter))
            return $"{at} ({t.TableName}): source '{t.Source}' is not configured. " +
                   $"Add it under 'sources:' (adapters available: {string.Join(", ", _adapters.Keys)}).";
        if (string.IsNullOrWhiteSpace(conn.Connector) && string.IsNullOrWhiteSpace(conn.Connection))
            return $"{at} ({t.TableName}): sources.{t.Source} needs 'connector' or 'connection'.";
        if (conn.Connection?.Contains("${") == true)
            return $"{at} ({t.TableName}): sources.{t.Source}.connection cannot use ${{VAR}}; use 'connector' with a shared expression.";
        if (adapter.ValidateTarget(t.Target) is { } targetError) return $"{at} ({t.TableName}): {targetError}";
        return null;
    }

    public GenerateReport Generate(ProjectConfig config, string configDir, bool dryRun)
    {
        var errors = Validate(config);
        var results = new List<TableGenResult>();
        for (var i = 0; i < config.Tables.Count; i++)
        {
            var spec = config.Tables[i];
            if (errors[i] != null) { results.Add(new(spec.TableName, "error", errors[i])); continue; }
            try { results.Add(new(spec.TableName, GenerateTable(config, spec, configDir, dryRun))); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
            {
                results.Add(new(spec.TableName, "error", ex.Message));
            }
        }

        var ok = results.Where(r => r.Status != "error").Select(r => r.Table);
        return new GenerateReport(results, AddRefs(config, configDir, ok, dryRun));
    }

    private string GenerateTable(ProjectConfig config, TableGenSpec spec, string configDir, bool dryRun)
    {
        var adapter = _adapters[spec.Source];
        var path = Path.Combine(configDir, "tables", FileNameSanitizer.SanitizeToLower(spec.TableName) + ".yaml");

        // Never fall back to generated-only when an existing file is unreadable: that would overwrite manual work.
        var existing = File.Exists(path) ? _yaml.LoadFromFile<TableDefinition>(path) : null;

        var meta = adapter.GetTableMetadata(spec.Target);
        if (meta.Columns.Count == 0)
            throw new InvalidOperationException($"No columns returned for target '{spec.Target}'.");
        var generated = new TableDefinition
        {
            Name = spec.TableName,
            Description = meta.Description,
            Source = adapter.ToSource(spec.Target, config.Sources[spec.Source]),
            Columns = meta.Columns
        };

        // Merger decides column-level merging; every other table-level property comes from the existing file as-is.
        var table = generated;
        if (existing != null)
        {
            existing.Columns = new TableMerger(new MergeOptions()).MergeTable(generated, path).Columns;
            existing.Source = generated.Source;
            existing.Description ??= generated.Description;
            table = existing;
        }

        var yaml = _yaml.Serialize(table);
        var status = existing == null ? "created" : File.ReadAllText(path) == yaml ? "unchanged" : "updated";
        if (!dryRun && status != "unchanged")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, yaml);
        }
        return status;
    }

    private List<string> AddRefs(ProjectConfig config, string configDir, IEnumerable<string> tables, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(config.Model)) return new();
        var modelPath = Path.Combine(configDir, config.Model);
        var model = _yaml.LoadFromFile<ModelDefinition>(modelPath);
        var missing = tables.Where(t => model.Tables.All(r => r.Ref != t)).ToList();
        if (missing.Count > 0 && !dryRun)
        {
            model.Tables.AddRange(missing.Select(t => new TableReference { Ref = t }));
            _yaml.SaveToFile(model, modelPath); // ponytail: round-trip drops YAML comments in the model file
        }
        return missing;
    }
}
