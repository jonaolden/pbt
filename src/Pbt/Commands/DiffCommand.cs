using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Commands;

public static class DiffCommand
{
    public static Command Create()
    {
        var pathAArgument = new Argument<string>(
            "path-a",
            "First project path (e.g., HEAD~1 for git-based comparison)");

        var pathBArgument = new Argument<string>(
            "path-b",
            "Second project path (e.g., HEAD for current state)");

        var breakingOption = new Option<bool>(
            "--breaking",
            "Return non-zero exit code if breaking changes are detected");

        var outputOption = new Option<string?>(
            "--output",
            "Output format: text (default) or json");

        var command = new Command("diff", "Compare two project states and classify changes")
        {
            pathAArgument,
            pathBArgument,
            breakingOption,
            outputOption
        };

        command.SetHandler((pathA, pathB, breakingOnly, outputFormat) =>
        {
            try
            {
                ExecuteDiff(pathA, pathB, breakingOnly, outputFormat);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Diff failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, pathAArgument, pathBArgument, breakingOption, outputOption);

        return command;
    }

    private static void ExecuteDiff(string pathA, string pathB, bool breakingOnly, string? outputFormat)
    {
        var serializer = new YamlSerializer();
        var changes = new List<DiffChange>();

        // Load tables from both paths
        var tablesA = LoadTables(pathA, serializer);
        var tablesB = LoadTables(pathB, serializer);

        // Load models from both paths
        var modelsA = LoadModels(pathA, serializer);
        var modelsB = LoadModels(pathB, serializer);

        // Compare tables
        CompareTableSets(tablesA, tablesB, changes);

        // Compare models
        CompareModelSets(modelsA, modelsB, changes);

        // Output results
        var hasBreaking = changes.Any(c => c.IsBreaking);

        if (outputFormat?.Equals("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            OutputJson(changes);
        }
        else
        {
            OutputText(changes);
        }

        if (breakingOnly && hasBreaking)
        {
            Environment.Exit(1);
        }
    }

    private static Dictionary<string, TableDefinition> LoadTables(string path, YamlSerializer serializer)
    {
        var tables = new Dictionary<string, TableDefinition>(StringComparer.OrdinalIgnoreCase);
        var tablesPath = Path.Combine(path, "tables");

        if (!Directory.Exists(tablesPath)) return tables;

        foreach (var file in Directory.GetFiles(tablesPath, "*.yaml").Concat(Directory.GetFiles(tablesPath, "*.yml")))
        {
            try
            {
                var table = serializer.LoadFromFile<TableDefinition>(file);
                tables[table.Name] = table;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: skipping unparseable table file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        return tables;
    }

    private static Dictionary<string, ModelDefinition> LoadModels(string path, YamlSerializer serializer)
    {
        var models = new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase);
        var modelsPath = Path.Combine(path, "models");

        if (!Directory.Exists(modelsPath)) return models;

        foreach (var file in Directory.GetFiles(modelsPath, "*.yaml").Concat(Directory.GetFiles(modelsPath, "*.yml")))
        {
            try
            {
                var model = serializer.LoadFromFile<ModelDefinition>(file);
                models[model.Name] = model;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: skipping unparseable model file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        return models;
    }

    private static void CompareTableSets(
        Dictionary<string, TableDefinition> tablesA,
        Dictionary<string, TableDefinition> tablesB,
        List<DiffChange> changes)
    {
        // Tables removed (breaking)
        foreach (var (name, table) in tablesA)
        {
            if (!tablesB.ContainsKey(name))
            {
                changes.Add(new DiffChange("table_removed", name, null, null, IsBreaking: true));
            }
        }

        // Tables added (non-breaking)
        foreach (var (name, table) in tablesB)
        {
            if (!tablesA.ContainsKey(name))
            {
                changes.Add(new DiffChange("table_added", name, null, null, IsBreaking: false));
            }
        }

        // Tables modified
        foreach (var (name, tableB) in tablesB)
        {
            if (!tablesA.TryGetValue(name, out var tableA)) continue;

            // Compare columns
            var colsA = tableA.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var colsB = tableB.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var (colName, _) in colsA)
            {
                if (!colsB.ContainsKey(colName))
                {
                    changes.Add(new DiffChange("column_removed", $"{name}.{colName}", null, null, IsBreaking: true));
                }
            }

            foreach (var (colName, colB) in colsB)
            {
                if (!colsA.TryGetValue(colName, out var colA))
                {
                    changes.Add(new DiffChange("column_added", $"{name}.{colName}", null, colB.Type, IsBreaking: false));
                }
                else if (!colA.Type.Equals(colB.Type, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new DiffChange("column_type_changed", $"{name}.{colName}", colA.Type, colB.Type, IsBreaking: true));
                }
                else
                {
                    // Non-breaking column changes
                    if (colA.Description != colB.Description)
                        changes.Add(new DiffChange("column_description_changed", $"{name}.{colName}", colA.Description, colB.Description, IsBreaking: false));
                    if (colA.FormatString != colB.FormatString)
                        changes.Add(new DiffChange("column_format_changed", $"{name}.{colName}", colA.FormatString, colB.FormatString, IsBreaking: false));
                }
            }

            // Compare measures
            var measA = tableA.Measures.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
            var measB = tableB.Measures.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var (measName, _) in measA)
            {
                if (!measB.ContainsKey(measName))
                    changes.Add(new DiffChange("measure_removed", $"{name}.[{measName}]", null, null, IsBreaking: true));
            }

            foreach (var (measName, measBDef) in measB)
            {
                if (!measA.ContainsKey(measName))
                    changes.Add(new DiffChange("measure_added", $"{name}.[{measName}]", null, null, IsBreaking: false));
                else if (measA[measName].Expression != measBDef.Expression)
                    changes.Add(new DiffChange("measure_expression_changed", $"{name}.[{measName}]", null, null, IsBreaking: false));
            }
        }
    }

    private static void CompareModelSets(
        Dictionary<string, ModelDefinition> modelsA,
        Dictionary<string, ModelDefinition> modelsB,
        List<DiffChange> changes)
    {
        foreach (var (name, _) in modelsA)
        {
            if (!modelsB.ContainsKey(name))
                changes.Add(new DiffChange("model_removed", name, null, null, IsBreaking: true));
        }

        foreach (var (name, modelB) in modelsB)
        {
            if (!modelsA.ContainsKey(name))
            {
                changes.Add(new DiffChange("model_added", name, null, null, IsBreaking: false));
                continue;
            }

            var modelA = modelsA[name];

            // Compare relationships
            var relsA = new HashSet<string>(modelA.Relationships.Select(r => $"{r.FromTable}.{r.FromColumn}->{r.ToTable}.{r.ToColumn}"));
            var relsB = new HashSet<string>(modelB.Relationships.Select(r => $"{r.FromTable}.{r.FromColumn}->{r.ToTable}.{r.ToColumn}"));

            foreach (var rel in relsA.Except(relsB))
                changes.Add(new DiffChange("relationship_removed", $"{name}: {rel}", null, null, IsBreaking: true));

            foreach (var rel in relsB.Except(relsA))
                changes.Add(new DiffChange("relationship_added", $"{name}: {rel}", null, null, IsBreaking: false));
        }
    }

    private static void OutputText(List<DiffChange> changes)
    {
        if (changes.Count == 0)
        {
            Console.WriteLine("No changes detected.");
            return;
        }

        var breakingChanges = changes.Where(c => c.IsBreaking).ToList();
        var nonBreakingChanges = changes.Where(c => !c.IsBreaking).ToList();

        if (breakingChanges.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Breaking changes ({breakingChanges.Count}):");
            Console.ResetColor();
            foreach (var change in breakingChanges)
            {
                Console.WriteLine($"  ✗ [{change.ChangeType}] {change.ObjectPath}");
                if (change.OldValue != null || change.NewValue != null)
                    Console.WriteLine($"    {change.OldValue ?? "(none)"} -> {change.NewValue ?? "(none)"}");
            }
            Console.WriteLine();
        }

        if (nonBreakingChanges.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Non-breaking changes ({nonBreakingChanges.Count}):");
            Console.ResetColor();
            foreach (var change in nonBreakingChanges)
            {
                Console.WriteLine($"  + [{change.ChangeType}] {change.ObjectPath}");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"Total: {changes.Count} change(s), {breakingChanges.Count} breaking");
    }

    private static void OutputJson(List<DiffChange> changes)
    {
        var result = new
        {
            total = changes.Count,
            breaking_count = changes.Count(c => c.IsBreaking),
            non_breaking_count = changes.Count(c => !c.IsBreaking),
            changes = changes.Select(c => new
            {
                change_type = c.ChangeType,
                object_path = c.ObjectPath,
                old_value = c.OldValue,
                new_value = c.NewValue,
                is_breaking = c.IsBreaking
            })
        };

        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }));
    }

    private record DiffChange(string ChangeType, string ObjectPath, string? OldValue, string? NewValue, bool IsBreaking = false);
}
