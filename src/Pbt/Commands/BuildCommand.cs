using System.CommandLine;
using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;
using Pbt.Infrastructure;

namespace Pbt.Commands;

public static class BuildCommand
{
    public static Command Create()
    {
        var projectPathArgument = new Argument<string>(
            "project-path",
            () => ".",
            "Path to the project directory or a model YAML file (defaults to current directory)");

        var modelOption = new Option<string?>(
            "--model",
            "Build specific model only (optional)");

        var outputOption = new Option<string?>(
            "--output",
            "Override output directory");

        var noLineageTagsOption = new Option<bool>(
            "--no-lineage-tags",
            "Skip lineage tag generation (WARNING: breaks connected reports)");

        var confirmOption = new Option<bool>(
            "--confirm",
            "Confirm potentially destructive operations (required with --no-lineage-tags)");

        var envOption = new Option<string?>(
            "--env",
            "Named environment to use (loads from environments/<name>.env.yml)");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            "Validate and compose model without writing output files");

        var preHookOption = new Option<string?>(
            "--pre-hook",
            "Shell command to execute before building (e.g., a transformation script)");

        var command = new Command("build", "Build Power BI project (.pbip) from YAML definitions (use 'build model' for TMDL-only output)")
        {
            projectPathArgument,
            modelOption,
            outputOption,
            noLineageTagsOption,
            confirmOption,
            envOption,
            dryRunOption,
            preHookOption
        };

        // Default handler: `pbt build [path]` produces PBIP output
        command.SetHandler((context) =>
        {
            var projectPath = context.ParseResult.GetValueForArgument(projectPathArgument);
            var modelName = context.ParseResult.GetValueForOption(modelOption);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            var noLineageTags = context.ParseResult.GetValueForOption(noLineageTagsOption);
            var confirm = context.ParseResult.GetValueForOption(confirmOption);
            var envName = context.ParseResult.GetValueForOption(envOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var preHook = context.ParseResult.GetValueForOption(preHookOption);

            try
            {
                if (!ValidateLineageFlags(noLineageTags, confirm)) return;
                ExecuteBuild(projectPath, modelName, outputPath, noLineageTags, envName, dryRun, preHook, OutputFormat.Pbip);
            }
            catch (Exception ex)
            {
                PrintError("Build failed", ex);
                Environment.Exit(1);
            }
        });

        command.AddCommand(CreateModelSubcommand());
        return command;
    }

    private static Command CreateModelSubcommand()
    {
        var projectPathArgument = new Argument<string>("project-path", () => ".", "Path to the project directory or a model YAML file");
        var modelOption = new Option<string?>("--model", "Build specific model only");
        var outputOption = new Option<string?>("--output", "Override output directory");
        var noLineageTagsOption = new Option<bool>("--no-lineage-tags", "Skip lineage tag generation");
        var confirmOption = new Option<bool>("--confirm", "Confirm potentially destructive operations");
        var envOption = new Option<string?>("--env", "Named environment to use");
        var dryRunOption = new Option<bool>("--dry-run", "Validate without writing files");
        var preHookOption = new Option<string?>("--pre-hook", "Shell command to execute before building");

        var command = new Command("model", "Build TMDL semantic model from YAML definitions")
        {
            projectPathArgument, modelOption, outputOption, noLineageTagsOption,
            confirmOption, envOption, dryRunOption, preHookOption
        };

        command.SetHandler((context) =>
        {
            var projectPath = context.ParseResult.GetValueForArgument(projectPathArgument);
            var modelName = context.ParseResult.GetValueForOption(modelOption);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            var noLineageTags = context.ParseResult.GetValueForOption(noLineageTagsOption);
            var confirm = context.ParseResult.GetValueForOption(confirmOption);
            var envName = context.ParseResult.GetValueForOption(envOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var preHook = context.ParseResult.GetValueForOption(preHookOption);

            try
            {
                if (!ValidateLineageFlags(noLineageTags, confirm)) return;
                ExecuteBuild(projectPath, modelName, outputPath, noLineageTags, envName, dryRun, preHook, OutputFormat.Tmdl);
            }
            catch (Exception ex)
            {
                PrintError("Build failed", ex);
                Environment.Exit(1);
            }
        });

        return command;
    }

    private enum OutputFormat { Pbip, Tmdl }

    private static void ExecuteBuild(string inputPath, string? modelName, string? outputPath, bool noLineageTags, string? envName, bool dryRun, string? preHook, OutputFormat format)
    {
        var (projectPath, modelFilter) = PathResolver.Resolve(inputPath);
        var effectiveModelFilter = modelName ?? modelFilter;

        Console.WriteLine($"Building project: {projectPath}");
        Console.WriteLine();

        // Execute pre-hook
        if (!string.IsNullOrWhiteSpace(preHook))
            RunPreHook(preHook, projectPath);

        var serializer = new YamlSerializer();
        var buildService = new BuildService(serializer);

        // Setup lineage
        LineageManifestService? lineageService = null;
        if (!noLineageTags)
        {
            lineageService = new LineageManifestService(serializer);
            lineageService.LoadManifest(projectPath);
        }

        // Load environment
        EnvironmentDefinition? environment = null;
        if (!string.IsNullOrWhiteSpace(envName))
        {
            environment = buildService.LoadEnvironment(projectPath, envName);
            Console.WriteLine($"Environment: {environment.Name}");
            Console.WriteLine($"  Expression overrides: {environment.Expressions.Count}");
            Console.WriteLine();
        }

        // Build
        var results = buildService.Build(projectPath, effectiveModelFilter, lineageService, environment);

        Console.WriteLine($"Built {results.Count} model(s)");
        foreach (var result in results)
        {
            Console.WriteLine($"  • {result.ModelName}");
        }
        Console.WriteLine();

        if (dryRun)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Dry run completed - no files written.");
            Console.ResetColor();
            return;
        }

        // Generate output
        var targetPath = outputPath ?? Path.Combine(projectPath, "target");

        foreach (var result in results)
        {
            switch (format)
            {
                case OutputFormat.Pbip:
                    WritePbipOutput(result.Database, result.ModelName, targetPath, lineageService);
                    break;
                case OutputFormat.Tmdl:
                    WriteTmdlOutput(result.Database, result.ModelName, targetPath, lineageService);
                    break;
            }
        }

        // Save lineage
        if (lineageService != null)
        {
            lineageService.SaveManifest(projectPath);
            if (lineageService.NewTagCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Lineage manifest updated with {lineageService.NewTagCount} new tag(s)");
                Console.ResetColor();
            }
        }

        // Display collision warnings
        if (lineageService?.CollisionWarnings.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (var warning in lineageService.CollisionWarnings)
                Console.WriteLine(warning);
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✓ Build completed successfully");
        Console.ResetColor();
    }

    #region Output Writers

    private static void WriteTmdlOutput(Database database, string projectName, string targetPath, LineageManifestService? lineageService)
    {
        var sanitizedName = FileNameSanitizer.Sanitize(projectName);
        var modelOutputPath = Path.Combine(targetPath, sanitizedName);

        if (Directory.Exists(modelOutputPath))
        {
            Console.WriteLine($"  Cleaning: {modelOutputPath}");
            Directory.Delete(modelOutputPath, true);
        }
        Directory.CreateDirectory(modelOutputPath);

        TmdlSerializer.SerializeDatabaseToFolder(database, modelOutputPath);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ Generated: {modelOutputPath}");
        Console.ResetColor();
        PrintLineageStats(lineageService);
    }

    private static void WritePbipOutput(Database database, string projectName, string targetPath, LineageManifestService? lineageService)
    {
        Console.WriteLine("Generating PBIP structure:");
        PbipGenerator.GeneratePbipStructure(database, projectName, targetPath);

        var sanitizedName = FileNameSanitizer.Sanitize(projectName);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ Created: {Path.Combine(targetPath, $"{sanitizedName}.pbip")}");
        Console.WriteLine($"  ✓ Created: {Path.Combine(targetPath, $"{sanitizedName}.SemanticModel/")} (TMDL files)");
        Console.WriteLine($"  ✓ Created: {Path.Combine(targetPath, $"{sanitizedName}.Report/")} (PBIR files)");
        Console.ResetColor();
        PrintLineageStats(lineageService);

        var validationErrors = PbipGenerator.ValidatePbipStructure(targetPath, projectName);
        if (validationErrors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nPBIP validation errors:");
            foreach (var error in validationErrors)
                Console.WriteLine($"  ✗ {error}");
            Console.ResetColor();
            throw new InvalidOperationException($"PBIP structure validation failed with {validationErrors.Count} error(s)");
        }

        Console.WriteLine();
    }

    #endregion

    #region Helpers

    private static bool ValidateLineageFlags(bool noLineageTags, bool confirm)
    {
        if (noLineageTags && !confirm)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: Building without lineage tags will break all connected Power BI reports.");
            Console.WriteLine("Add --confirm to proceed, or remove --no-lineage-tags.");
            Console.ResetColor();
            Environment.Exit(1);
            return false;
        }

        if (noLineageTags)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: Building without lineage tags. Connected reports will break.");
            Console.ResetColor();
            Console.WriteLine();
        }

        return true;
    }

    private static void RunPreHook(string preHook, string projectPath)
    {
        Console.WriteLine($"Executing pre-hook: {preHook}");
        var hookProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? $"/c {preHook}" : $"-c {preHook}",
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (hookProcess == null)
            throw new InvalidOperationException("Failed to start pre-hook process");

        var stdoutTask = hookProcess.StandardOutput.ReadToEndAsync();
        var stderrTask = hookProcess.StandardError.ReadToEndAsync();

        const int hookTimeoutMs = 60_000;
        if (!hookProcess.WaitForExit(hookTimeoutMs))
        {
            hookProcess.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"Pre-hook timed out after {hookTimeoutMs / 1000}s and was terminated");
        }

        var stderr = stderrTask.Result;
        if (hookProcess.ExitCode != 0)
            throw new InvalidOperationException($"Pre-hook failed with exit code {hookProcess.ExitCode}: {stderr}");

        Console.WriteLine("Pre-hook completed.");
        Console.WriteLine();
    }

    private static void PrintLineageStats(LineageManifestService? lineageService)
    {
        if (lineageService != null)
            Console.WriteLine($"  Lineage tags: {lineageService.NewTagCount} new, {lineageService.ExistingTagCount} existing");
    }

    private static void PrintError(string context, Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n✗ {context}: {ex.Message}");
        Console.ResetColor();
    }

    #endregion
}
