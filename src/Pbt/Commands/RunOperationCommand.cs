using System.CommandLine;
using System.Text.Json;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;
using Pbt.Infrastructure;

namespace Pbt.Commands;

public static class RunOperationCommand
{
    public static Command Create()
    {
        var macroNameArgument = new Argument<string>(
            "macro-name",
            "Name of the macro to execute (without .yaml extension)");

        var argsOption = new Option<string>(
            "--args",
            () => "{}",
            "JSON string with macro arguments");

        var projectPathOption = new Option<string?>(
            "--project-path",
            "Path to the project directory (overrides auto-discovery)");

        var command = new Command("run-operation", "Execute a macro operation on YAML files")
        {
            macroNameArgument,
            argsOption,
            projectPathOption
        };

        command.SetHandler((macroName, argsJson, projectPath) =>
        {
            try
            {
                ExecuteRunOperation(macroName, argsJson, projectPath);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Error: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, macroNameArgument, argsOption, projectPathOption);

        return command;
    }

    private static void ExecuteRunOperation(string macroName, string argsJson, string? projectPathOverride)
    {
        // Parse args
        RunOperationArgs args;
        try
        {
            args = JsonSerializer.Deserialize<RunOperationArgs>(argsJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = new SnakeCaseNamingPolicy()
                }) ?? new RunOperationArgs();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON in --args: {ex.Message}", ex);
        }

        // Determine project root: explicit --project-path wins over auto-discovery
        string projectRoot;
        if (!string.IsNullOrWhiteSpace(projectPathOverride))
        {
            projectRoot = Path.IsPathRooted(projectPathOverride)
                ? projectPathOverride
                : Path.Combine(Directory.GetCurrentDirectory(), projectPathOverride);
        }
        else
        {
            // For merge operations, derive project root from target file path
            // Target path like "projects/wecare/tables/date.yaml" -> project root "projects/wecare"
            string? pathForDiscovery = args.Path;
            if (!string.IsNullOrWhiteSpace(args.Target))
            {
                pathForDiscovery = args.Target;
            }
            projectRoot = DiscoverProjectRoot(pathForDiscovery);
        }
        Console.WriteLine($"Project root: {projectRoot}");

        // Initialize services
        var serializer = new YamlSerializer();
        var assetLoader = new AssetLoader(serializer);
        var fileResolver = new MacroFileResolver();
        var executor = new PipelineExecutor();

        // Try to load macro paths from the first model's asset config, fall back to convention
        List<string> macroPaths;
        try
        {
            var modelFiles = assetLoader.FindModelFiles(projectRoot);
            if (modelFiles.Count > 0)
            {
                var firstModel = serializer.LoadFromFile<ModelDefinition>(modelFiles[0]);
                var assetPaths = assetLoader.ResolveAssetPaths(firstModel, projectRoot);
                macroPaths = assetLoader.GetMacroPaths(assetPaths);
            }
            else
            {
                var legacyMacrosPath = Path.Combine(projectRoot, "macros");
                macroPaths = Directory.Exists(legacyMacrosPath)
                    ? new List<string> { legacyMacrosPath }
                    : new List<string>();
            }
            Console.WriteLine($"Macro paths: {macroPaths.Count}");
            foreach (var path in macroPaths)
            {
                Console.WriteLine($"  - {path}");
            }
        }
        catch
        {
            var legacyMacrosPath = Path.Combine(projectRoot, "macros");
            macroPaths = Directory.Exists(legacyMacrosPath)
                ? new List<string> { legacyMacrosPath }
                : new List<string>();
        }

        // Load macro from configured paths
        var macroLoader = new MacroLoader(serializer);
        Console.WriteLine($"Loading macro: {macroName}");
        
        MacroDefinition macro;
        if (macroPaths.Count > 0)
        {
            macro = macroLoader.LoadMacroFromPaths(macroPaths, macroName);
        }
        else
        {
            macro = macroLoader.LoadMacro(projectRoot, macroName);
        }
        
        Console.WriteLine($"Macro loaded: {macro.Name}");
        if (!string.IsNullOrEmpty(macro.Description))
        {
            Console.WriteLine($"Description: {macro.Description}");
        }

        // Display macro type
        if (macro.Merge != null)
        {
            Console.WriteLine($"Operation type: Merge");
            Console.WriteLine($"Merge strategy: {macro.Merge.Strategy}");
            Console.WriteLine($"Target nodes: {macro.Merge.TargetNodes}");
            Console.WriteLine($"Identifier: {macro.Merge.Identifier}");
        }
        else if (macro.Macros != null && macro.Macros.Count > 0)
        {
            Console.WriteLine($"Operation type: Macro Pipeline");
            Console.WriteLine($"Referenced macros: {string.Join(", ", macro.Macros)}");
            Console.WriteLine($"Total pipeline steps (resolved): {macro.Pipeline.Count}");
        }
        else
        {
            Console.WriteLine($"Operation type: Pipeline");
            Console.WriteLine($"Pipeline steps: {macro.Pipeline.Count}");
        }
        Console.WriteLine();

        // Resolve target files (skip for merge operations)
        List<string> targetFiles = new();
        if (macro.Merge == null)
        {
            targetFiles = fileResolver.ResolveTargetFiles(projectRoot, macro, args);
            Console.WriteLine($"Target files: {targetFiles.Count}");
        }
        else
        {
            // For merge operations, display source and target
            if (!string.IsNullOrWhiteSpace(args.Source))
            {
                Console.WriteLine($"Source file: {args.Source}");
            }
            if (!string.IsNullOrWhiteSpace(args.Target))
            {
                Console.WriteLine($"Target file: {args.Target}");
            }
        }

        if (args.DryRun)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("DRY RUN MODE - No files will be modified");
            Console.ResetColor();
        }
        Console.WriteLine();

        // Execute macro
        Console.WriteLine("Executing macro...");
        var result = executor.ExecuteMacro(macro, targetFiles, args);

        // Display results
        Console.WriteLine();
        DisplayResults(result, args);

        if (args.DryRun)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Dry run completed. No files were modified.");
            Console.WriteLine("Run without dry_run to apply changes.");
            Console.ResetColor();
        }
        else if (result.FilesChanged > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Successfully modified {result.FilesChanged} file(s)");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No changes were made.");
            Console.ResetColor();
        }
    }

    private static void DisplayResults(MacroExecutionResult result, RunOperationArgs args)
    {
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Files processed: {result.FilesProcessed}");
        Console.WriteLine($"  Files changed: {result.FilesChanged}");
        Console.WriteLine($"  Nodes matched: {result.NodesMatched}");
        Console.WriteLine($"  Nodes changed: {result.NodesChanged}");

        if (result.FileChangeCounts.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Changes per file:");
            foreach (var (file, count) in result.FileChangeCounts.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  {Path.GetFileName(file)}: {count} change(s)");
            }
        }

        if (result.Changes.Count > 0)
        {
            Console.WriteLine();
            var limit = Math.Min(result.Changes.Count, args.PrintChangesLimit);
            Console.WriteLine($"Example changes (showing {limit} of {result.Changes.Count}):");

            var changesToShow = result.Changes.Take(limit);
            foreach (var change in changesToShow)
            {
                Console.WriteLine();
                Console.WriteLine($"  File: {Path.GetFileName(change.FilePath)}");
                Console.WriteLine($"  Path: {change.Path}");
                Console.WriteLine($"  Step: {change.StepId}");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  - {change.Before}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  + {change.After}");
                Console.ResetColor();
            }

            if (result.Changes.Count > limit)
            {
                Console.WriteLine();
                Console.WriteLine($"  ... and {result.Changes.Count - limit} more change(s)");
            }
        }
    }

    private static string DiscoverProjectRoot(string? providedPath)
    {
        var currentDir = Directory.GetCurrentDirectory();

        // Determine starting point for search
        string searchDir;
        if (!string.IsNullOrEmpty(providedPath))
        {
            // Resolve provided path to absolute path
            searchDir = Path.IsPathRooted(providedPath)
                ? providedPath
                : Path.Combine(currentDir, providedPath);

            // If it's a file, start from its directory
            if (File.Exists(searchDir))
            {
                searchDir = Path.GetDirectoryName(searchDir) ?? currentDir;
            }
            // If directory doesn't exist, fall back to current directory
            else if (!Directory.Exists(searchDir))
            {
                searchDir = currentDir;
            }

            // If we're in a "tables", "models", or similar subdirectory,
            // go up one level since project root is typically the parent
            var dirName = new DirectoryInfo(searchDir).Name.ToLower();
            if (dirName == "tables" || dirName == "models" || dirName == "macros")
            {
                var parentDir = Directory.GetParent(searchDir);
                if (parentDir != null)
                {
                    searchDir = parentDir.FullName;
                }
            }
        }
        else
        {
            searchDir = currentDir;
        }

        // Walk up directory tree looking for models/ or macros/ directory (max 10 levels)
        var originalSearchDir = searchDir;
        const int maxDepth = 10;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            var modelsDir = Path.Combine(searchDir, "models");
            var macrosDir = Path.Combine(searchDir, "macros");

            if (Directory.Exists(modelsDir) || Directory.Exists(macrosDir))
            {
                return searchDir;
            }

            var parentDir = Directory.GetParent(searchDir);
            if (parentDir == null)
            {
                // Reached filesystem root without finding project
                break;
            }

            searchDir = parentDir.FullName;
        }

        // Project not found within depth limit, use original search directory
        return originalSearchDir;
    }
}
