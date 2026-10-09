using System.CommandLine;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;
using Pbt.Infrastructure;

namespace Pbt.Commands;

public static class GenerateTablesCommand
{
    public static Command Create()
    {
        var configOption = new Option<string>("--config", () => "pbt.yml", "Path to pbt.yml (.yaml also accepted)");
        var dryRunOption = new Option<bool>("--dry-run", "Show what would change without writing files");
        var command = new Command("generate-tables", "Generate tables/*.yaml from pbt.yml source definitions")
        {
            configOption,
            dryRunOption
        };
        command.SetHandler((config, dryRun) => Run(() =>
        {
            var (cfg, dir) = Load(config);
            var report = new TableGenerator().Generate(cfg, dir, dryRun);
            var lines = report.Tables.Select(t => $"  {t.Status,-9} {t.Table}{(t.Error != null ? $": {t.Error}" : "")}")
                .Concat(report.RefsAdded.Select(r => $"  ref       {r}"));
            OutputFormatter.WriteResult(
                new { dry_run = dryRun, tables = report.Tables, refs_added = report.RefsAdded },
                (dryRun ? "Dry run (no files written):\n" : "") + string.Join("\n", lines));
            return report.HasErrors ? 1 : 0;
        }), configOption, dryRunOption);
        return command;
    }

    internal static (ProjectConfig Config, string Dir) Load(string path)
    {
        var full = Path.GetFullPath(path);
        var alt = Path.ChangeExtension(full, full.EndsWith(".yml") ? ".yaml" : ".yml");
        if (!File.Exists(full) && File.Exists(alt)) full = alt;
        if (!File.Exists(full))
            throw new FileNotFoundException(
                $"Config not found: {full}. Create a pbt.yml here (see docs/cli-reference.md#generate-tables) or pass --config <path>.");
        return (new YamlSerializer().LoadFromFile<ProjectConfig>(full), Path.GetDirectoryName(full)!);
    }

    internal static void Run(Func<int> action)
    {
        try
        {
            var code = action();
            if (code != 0) Environment.Exit(code);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ {ex.Message}");
            Environment.Exit(1);
        }
    }
}

public static class ValidateTablesCommand
{
    public static Command Create()
    {
        var configOption = new Option<string>("--config", () => "pbt.yml", "Path to pbt.yml (.yaml also accepted)");
        var command = new Command("validate-tables", "Validate pbt.yml table definitions (offline, no source lookup)") { configOption };
        command.SetHandler(config => GenerateTablesCommand.Run(() =>
        {
            var (cfg, _) = GenerateTablesCommand.Load(config);
            var errors = new TableGenerator().Validate(cfg).OfType<string>().ToList();
            OutputFormatter.WriteResult(new { valid = errors.Count == 0, errors },
                errors.Count == 0 ? $"✓ {cfg.Tables.Count} table definition(s) valid" : string.Join("\n", errors.Select(e => $"✗ {e}")));
            return errors.Count == 0 ? 0 : 1;
        }), configOption);
        return command;
    }
}
