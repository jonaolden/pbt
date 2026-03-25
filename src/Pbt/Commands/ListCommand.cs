using System.CommandLine;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Commands;

public static class ListCommand
{
    public static Command Create()
    {
        var projectPathArgument = new Argument<string>(
            "project-path",
            () => ".",
            "Path to the project directory (defaults to current directory)");

        var detailsOption = new Option<bool>(
            "--details",
            "Show detailed information about each table and model");

        var command = new Command("list", "List available tables and models")
        {
            projectPathArgument,
            detailsOption
        };

        command.SetHandler((projectPath, showDetails) =>
        {
            try
            {
                ExecuteList(projectPath, showDetails);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ List failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, projectPathArgument, detailsOption);

        return command;
    }

    private static void ExecuteList(string projectPath, bool showDetails)
    {
        // Validate project path
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        }

        var serializer = new YamlSerializer();
        var assetLoader = new AssetLoader(serializer);

        // Find model files by convention
        var modelFiles = assetLoader.FindModelFiles(projectPath);

        if (modelFiles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No model files found in models/ directory.");
            Console.ResetColor();
            return;
        }

        // Use the first model for project-level display info
        var firstModel = serializer.LoadFromFile<ModelDefinition>(modelFiles[0]);
        Console.WriteLine($"Project: {projectPath}");
        Console.WriteLine();

        // Resolve asset paths from first model to list tables
        var assetPaths = assetLoader.ResolveAssetPaths(firstModel, projectPath);

        // List tables from all configured paths
        if (assetPaths.TablePaths.Count > 0)
        {
            var registry = assetLoader.CreateTableRegistry(assetPaths);
            var tables = registry.ListTables();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Tables ({tables.Count}):");
            Console.ResetColor();

            if (tables.Count == 0)
            {
                Console.WriteLine("  (none)");
            }
            else
            {
                foreach (var tableName in tables.OrderBy(t => t))
                {
                    var table = registry.GetTable(tableName);
                    var sourceGroup = registry.GetTableSourceGroup(tableName);
                    Console.Write($"  - {table.Name}");

                    if (showDetails)
                    {
                        Console.WriteLine();
                        if (!string.IsNullOrWhiteSpace(table.Description))
                        {
                            Console.WriteLine($"      Description: {table.Description}");
                        }
                        Console.WriteLine($"      Columns: {table.Columns.Count}");
                        if (table.Hierarchies.Count > 0)
                        {
                            Console.WriteLine($"      Hierarchies: {table.Hierarchies.Count}");
                        }
                        if (!string.IsNullOrWhiteSpace(table.LineageTag))
                        {
                            Console.WriteLine($"      Lineage Tag: {table.LineageTag}");
                        }
                        if (!string.IsNullOrWhiteSpace(sourceGroup))
                        {
                            Console.WriteLine($"      Source Path: {sourceGroup}");
                        }
                    }
                    else
                    {
                        var details = new List<string>();
                        details.Add($"{table.Columns.Count} columns");
                        if (table.Hierarchies.Count > 0)
                        {
                            details.Add($"{table.Hierarchies.Count} hierarchies");
                        }
                        Console.WriteLine($" ({string.Join(", ", details)})");
                    }
                }
            }
            Console.WriteLine();
        }

        // List models
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Models ({modelFiles.Count}):");
        Console.ResetColor();

        if (modelFiles.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var modelFile in modelFiles.OrderBy(f => f))
            {
                var model = serializer.LoadFromFile<ModelDefinition>(modelFile);
                Console.Write($"  - {model.Name}");

                if (showDetails)
                {
                    Console.WriteLine();
                    if (!string.IsNullOrWhiteSpace(model.Description))
                    {
                        Console.WriteLine($"      Description: {model.Description}");
                    }
                    Console.WriteLine($"      Tables: {model.Tables.Count}");
                    if (model.Relationships.Count > 0)
                    {
                        Console.WriteLine($"      Relationships: {model.Relationships.Count}");
                    }
                    if (model.Measures.Count > 0)
                    {
                        Console.WriteLine($"      Measures: {model.Measures.Count}");
                    }
                    Console.WriteLine($"      Source: {modelFile}");
                }
                else
                {
                    var details = new List<string>();
                    details.Add($"{model.Tables.Count} tables");
                    if (model.Relationships.Count > 0)
                    {
                        details.Add($"{model.Relationships.Count} relationships");
                    }
                    if (model.Measures.Count > 0)
                    {
                        details.Add($"{model.Measures.Count} measures");
                    }
                    Console.WriteLine($" ({string.Join(", ", details)})");
                }
            }
        }
        Console.WriteLine();

        // Check for lineage manifest
        var lineageManifestPath = Path.Combine(projectPath, ".pbt", "lineage.yaml");
        if (File.Exists(lineageManifestPath))
        {
            var lineageService = new LineageManifestService(serializer);
            lineageService.LoadManifest(projectPath);
            var tableCount = lineageService.GetTables().Count();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Lineage:");
            Console.ResetColor();
            Console.WriteLine($"  Tracked tables: {tableCount}");
            Console.WriteLine($"  Manifest: .pbt/lineage.yaml");
        }
    }
}
