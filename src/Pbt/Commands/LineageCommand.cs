using System.CommandLine;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Commands;

public static class LineageCommand
{
    public static Command Create()
    {
        // Subcommand: show
        var showProjectPathArg = new Argument<string>(
            "project-path",
            () => ".",
            "Path to the project directory (defaults to current directory)");
        var detailsOption = new Option<bool>("--details", "Show detailed tag information");
        var showCommand = new Command("show", "Display current lineage manifest")
        {
            showProjectPathArg,
            detailsOption
        };
        showCommand.SetHandler((projectPath, showDetails) =>
        {
            try
            {
                ExecuteShow(projectPath, showDetails);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Show failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, showProjectPathArg, detailsOption);

        // Subcommand: clean
        var cleanProjectPathArg = new Argument<string>(
            "project-path",
            () => ".",
            "Path to the project directory (defaults to current directory)");
        var dryRunOption = new Option<bool>("--dry-run", "Show what would be removed without removing");
        var cleanCommand = new Command("clean", "Remove tags for objects no longer in project")
        {
            cleanProjectPathArg,
            dryRunOption
        };
        cleanCommand.SetHandler((projectPath, dryRun) =>
        {
            try
            {
                ExecuteClean(projectPath, dryRun);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Clean failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, cleanProjectPathArg, dryRunOption);

        // Subcommand: reset
        var resetProjectPathArg = new Argument<string>(
            "project-path",
            () => ".",
            "Path to the project directory (defaults to current directory)");
        var confirmOption = new Option<bool>("--confirm", "Confirm reset operation");
        var resetCommand = new Command("reset", "Delete manifest (next build generates all new tags)")
        {
            resetProjectPathArg,
            confirmOption
        };
        resetCommand.SetHandler((projectPath, confirm) =>
        {
            try
            {
                ExecuteReset(projectPath, confirm);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Reset failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, resetProjectPathArg, confirmOption);

        var command = new Command("lineage", "Manage lineage tag manifest");
        command.AddCommand(showCommand);
        command.AddCommand(cleanCommand);
        command.AddCommand(resetCommand);

        return command;
    }

    private static void ExecuteShow(string projectPath, bool showDetails)
    {
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        }

        var manifestPath = Path.Combine(projectPath, ".pbt", "lineage.yaml");
        if (!File.Exists(manifestPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No lineage manifest found.");
            Console.ResetColor();
            Console.WriteLine("Run 'pbt build' to generate lineage tags.");
            return;
        }

        var serializer = new YamlSerializer();
        var lineageService = new LineageManifestService(serializer);
        lineageService.LoadManifest(projectPath);

        var tables = lineageService.GetTables().ToList();

        Console.WriteLine($"Lineage Manifest: {manifestPath}");
        Console.WriteLine($"Tracked tables: {tables.Count}");
        Console.WriteLine();

        if (showDetails)
        {
            // Read the raw manifest to display all tags
            var manifest = serializer.LoadFromFile<LineageManifest>(manifestPath);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Lineage Tags:");
            Console.ResetColor();

            foreach (var table in tables.OrderBy(t => t))
            {
                Console.WriteLine($"\n  {table}");

                if (manifest.Tables.TryGetValue(table, out var tableInfo))
                {
                    Console.WriteLine($"    Table Tag: {tableInfo.Self}");

                    if (tableInfo.Columns?.Count > 0)
                    {
                        Console.WriteLine($"    Columns ({tableInfo.Columns.Count}):");
                        foreach (var col in tableInfo.Columns.OrderBy(c => c.Key))
                        {
                            Console.WriteLine($"      - {col.Key}: {col.Value}");
                        }
                    }

                    if (tableInfo.Measures?.Count > 0)
                    {
                        Console.WriteLine($"    Measures ({tableInfo.Measures.Count}):");
                        foreach (var measure in tableInfo.Measures.OrderBy(m => m.Key))
                        {
                            Console.WriteLine($"      - {measure.Key}: {measure.Value}");
                        }
                    }

                    if (tableInfo.Hierarchies?.Count > 0)
                    {
                        Console.WriteLine($"    Hierarchies ({tableInfo.Hierarchies.Count}):");
                        foreach (var hierarchy in tableInfo.Hierarchies.OrderBy(h => h.Key))
                        {
                            Console.WriteLine($"      - {hierarchy.Key}: {hierarchy.Value}");
                        }
                    }
                }
            }
        }
        else
        {
            var manifest = serializer.LoadFromFile<LineageManifest>(manifestPath);

            Console.WriteLine("Tables:");
            foreach (var table in tables.OrderBy(t => t))
            {
                if (manifest.Tables.TryGetValue(table, out var tableInfo))
                {
                    var objectCount = 1 + // table itself
                        (tableInfo.Columns?.Count ?? 0) +
                        (tableInfo.Measures?.Count ?? 0) +
                        (tableInfo.Hierarchies?.Count ?? 0);
                    Console.WriteLine($"  • {table} ({objectCount} tags)");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Use --details to see all lineage tags");
        }
    }

    private static void ExecuteClean(string projectPath, bool dryRun)
    {
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        }

        var manifestPath = Path.Combine(projectPath, ".pbt", "lineage.yaml");
        if (!File.Exists(manifestPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No lineage manifest found. Nothing to clean.");
            Console.ResetColor();
            return;
        }

        var serializer = new YamlSerializer();

        // Load table registry
        var tablesPath = Path.Combine(projectPath, "tables");
        var registry = new TableRegistry(serializer);
        registry.LoadTables(tablesPath);

        // Load models
        var modelsPath = Path.Combine(projectPath, "models");
        var modelFiles = Directory.GetFiles(modelsPath, "*.yaml")
            .Concat(Directory.GetFiles(modelsPath, "*.yml"))
            .ToList();

        var models = new List<ModelDefinition>();
        foreach (var modelFile in modelFiles)
        {
            models.Add(serializer.LoadFromFile<ModelDefinition>(modelFile));
        }

        // Clean orphaned tags
        var lineageService = new LineageManifestService(serializer);
        lineageService.LoadManifest(projectPath);

        if (dryRun)
        {
            Console.WriteLine($"Dry run: Showing what would be removed");
            Console.WriteLine();
        }

        var removedCount = lineageService.CleanOrphanedTags(registry, models);

        if (removedCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Removed {removedCount} orphaned tag(s)");
            Console.ResetColor();

            if (!dryRun)
            {
                lineageService.SaveManifest(projectPath);
                Console.WriteLine("Lineage manifest updated");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("No orphaned tags found");
            Console.ResetColor();
        }
    }

    private static void ExecuteReset(string projectPath, bool confirm)
    {
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        }

        var manifestPath = Path.Combine(projectPath, ".pbt", "lineage.yaml");
        if (!File.Exists(manifestPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No lineage manifest found. Nothing to reset.");
            Console.ResetColor();
            return;
        }

        if (!confirm)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: This will delete the lineage manifest.");
            Console.WriteLine("The next build will generate all new lineage tags.");
            Console.WriteLine("This will break existing Power BI reports that reference this model.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("To confirm, run: pbt lineage reset <project-path> --confirm");
            return;
        }

        File.Delete(manifestPath);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Lineage manifest deleted");
        Console.ResetColor();
        Console.WriteLine("Next build will generate new lineage tags for all objects");
    }
}
