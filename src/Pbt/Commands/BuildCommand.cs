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
                // Warn about --no-lineage-tags
                if (noLineageTags && !confirm)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WARNING: Building without lineage tags will break all connected Power BI reports.");
                    Console.WriteLine("Add --confirm to proceed, or remove --no-lineage-tags.");
                    Console.ResetColor();
                    Environment.Exit(1);
                    return;
                }

                if (noLineageTags)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WARNING: Building without lineage tags. Connected reports will break.");
                    Console.ResetColor();
                    Console.WriteLine();
                }

                ExecuteProjectBuild(projectPath, modelName, outputPath, noLineageTags, envName, dryRun, preHook);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Build failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        });

        // Add subcommands
        command.AddCommand(CreateModelSubcommand());

        return command;
    }

    #region Model Subcommand (TMDL Only)

    private static Command CreateModelSubcommand()
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
            "Skip lineage tag generation");

        var confirmOption = new Option<bool>(
            "--confirm",
            "Confirm potentially destructive operations");

        var envOption = new Option<string?>(
            "--env",
            "Named environment to use");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            "Validate without writing files");

        var preHookOption = new Option<string?>(
            "--pre-hook",
            "Shell command to execute before building");

        var command = new Command("model", "Build TMDL semantic model from YAML definitions")
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
                if (noLineageTags && !confirm)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WARNING: Building without lineage tags will break all connected Power BI reports.");
                    Console.WriteLine("Add --confirm to proceed, or remove --no-lineage-tags.");
                    Console.ResetColor();
                    Environment.Exit(1);
                    return;
                }

                ExecuteModelBuild(projectPath, modelName, outputPath, noLineageTags, envName, dryRun, preHook);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Build failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static void ExecuteModelBuild(string inputPath, string? modelName, string? outputPath, bool noLineageTags, string? envName = null, bool dryRun = false, string? preHook = null)
    {
        var models = ExecuteCoreBuild(inputPath, modelName, noLineageTags, out var lineageService, out var projectRoot, envName, preHook);

        if (dryRun)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nDry run completed - no files written.");
            Console.ResetColor();
            return;
        }

        var targetPath = outputPath ?? Path.Combine(projectRoot, "target");

        foreach (var (database, _, modelName_) in models)
        {
            GenerateTmdlOutput(database, modelName_, targetPath, lineageService);
        }

        SaveLineageManifest(lineageService, projectRoot);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✓ Build completed successfully");
        Console.ResetColor();
    }

    #endregion

    private static void ExecuteProjectBuild(string inputPath, string? modelName, string? outputPath, bool noLineageTags, string? envName = null, bool dryRun = false, string? preHook = null)
    {
        var models = ExecuteCoreBuild(inputPath, modelName, noLineageTags, out var lineageService, out var projectRoot, envName, preHook);

        if (dryRun)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nDry run completed - no files written.");
            Console.ResetColor();
            return;
        }

        var targetPath = outputPath ?? Path.Combine(projectRoot, "target");

        foreach (var (database, _, modelName_) in models)
        {
            GeneratePbipOutput(database, modelName_, targetPath, lineageService);
        }

        SaveLineageManifest(lineageService, projectRoot);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✓ Build completed successfully");
        Console.ResetColor();
    }

    #region Shared Build Logic

    private static List<(Database database, string modelFileName, string modelName)> ExecuteCoreBuild(
        string inputPath,
        string? modelName,
        bool noLineageTags,
        out LineageManifestService? lineageService,
        out string projectRoot,
        string? envName = null,
        string? preHook = null)
    {
        // Resolve input: may be a project directory or a model YAML file
        var (projectPath, modelFilter) = PathResolver.Resolve(inputPath);
        projectRoot = projectPath;

        Console.WriteLine($"Building project: {projectPath}");
        Console.WriteLine();

        // Execute pre-hook if specified
        if (!string.IsNullOrWhiteSpace(preHook))
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
            {
                throw new InvalidOperationException("Failed to start pre-hook process");
            }

            // Read streams before WaitForExit to avoid deadlock with redirected output
            var stdoutTask = hookProcess.StandardOutput.ReadToEndAsync();
            var stderrTask = hookProcess.StandardError.ReadToEndAsync();

            const int hookTimeoutMs = 60_000; // 60 second timeout
            if (!hookProcess.WaitForExit(hookTimeoutMs))
            {
                hookProcess.Kill(entireProcessTree: true);
                throw new InvalidOperationException($"Pre-hook timed out after {hookTimeoutMs / 1000}s and was terminated");
            }

            var stderr = stderrTask.Result;
            if (hookProcess.ExitCode != 0)
            {
                throw new InvalidOperationException($"Pre-hook failed with exit code {hookProcess.ExitCode}: {stderr}");
            }
            Console.WriteLine("Pre-hook completed.");
            Console.WriteLine();
        }

        var serializer = new YamlSerializer();
        var assetLoader = new AssetLoader(serializer);
        var validator = new Validator(serializer);

        // 1. Find model files by convention (models/ subdirectory)
        var modelFiles = assetLoader.FindModelFiles(projectPath);

        // Apply model filter: --model flag takes priority, then file-based filter from PathResolver
        var effectiveModelFilter = modelName ?? modelFilter;
        if (effectiveModelFilter != null)
        {
            modelFiles = modelFiles.Where(f =>
                Path.GetFileNameWithoutExtension(f).Equals(effectiveModelFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (modelFiles.Count == 0)
            {
                throw new FileNotFoundException($"Model '{effectiveModelFilter}' not found in models/ directory");
            }
        }

        if (modelFiles.Count == 0)
        {
            throw new FileNotFoundException($"No model files found in '{Path.Combine(projectPath, "models")}'");
        }

        Console.WriteLine($"Building {modelFiles.Count} model(s)");
        Console.WriteLine();

        // 2. Setup lineage service if needed
        lineageService = null;
        if (!noLineageTags)
        {
            lineageService = new LineageManifestService(serializer);
            lineageService.LoadManifest(projectPath);
        }

        // 3. Load environment if specified
        EnvironmentDefinition? environment = null;
        if (!string.IsNullOrWhiteSpace(envName))
        {
            environment = LoadEnvironment(projectPath, envName, serializer);
            Console.WriteLine($"Environment: {environment.Name}");
            Console.WriteLine($"  Expression overrides: {environment.Expressions.Count}");
            Console.WriteLine();
        }

        var models = new List<(Database, string, string)>();

        // 4. Build each model (each model is self-contained with its own asset config)
        foreach (var modelFile in modelFiles)
        {
            var modelDef = serializer.LoadFromFile<ModelDefinition>(modelFile);
            var modelFileName = Path.GetFileNameWithoutExtension(modelFile);

            Console.WriteLine($"Building model: {modelDef.Name}");
            if (!string.IsNullOrWhiteSpace(modelDef.Description))
                Console.WriteLine($"  Description: {modelDef.Description}");
            Console.WriteLine($"  Compatibility Level: {modelDef.CompatibilityLevel}");
            Console.WriteLine($"  Tables: {modelDef.Tables.Count}");
            Console.WriteLine($"  Relationships: {modelDef.Relationships.Count}");
            Console.WriteLine($"  Measures: {modelDef.Measures.Count}");
            if (modelDef.CalculationGroups?.Count > 0)
                Console.WriteLine($"  Calculation Groups: {modelDef.CalculationGroups.Count}");
            if (modelDef.Perspectives?.Count > 0)
                Console.WriteLine($"  Perspectives: {modelDef.Perspectives.Count}");
            if (modelDef.Roles?.Count > 0)
                Console.WriteLine($"  Roles: {modelDef.Roles.Count}");
            Console.WriteLine();

            // Resolve asset paths for this model
            var assetPaths = assetLoader.ResolveAssetPaths(modelDef, projectPath);

            Console.WriteLine("Asset paths (by priority):");
            Console.WriteLine($"  Tables: {assetPaths.TablePaths.Count} path(s)");
            foreach (var path in assetPaths.TablePaths)
                Console.WriteLine($"    - {path}");
            Console.WriteLine();

            // Validate model
            var validationResult = validator.ValidateProjectWithAssets(modelDef, modelFile, assetPaths);

            if (!validationResult.IsValid)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(validationResult.FormatMessages());
                Console.ResetColor();
                throw new InvalidOperationException($"Validation failed for model '{modelDef.Name}'");
            }

            if (validationResult.HasWarnings)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(validationResult.FormatMessages());
                Console.ResetColor();
                Console.WriteLine();
            }

            // Load table registry
            var registry = assetLoader.CreateTableRegistry(assetPaths);
            Console.WriteLine($"Loaded {registry.ListTables().Count} table definitions");
            Console.WriteLine();

            // Scan for connector configs
            var connectorConfigs = ScanForConnectorConfigs(projectPath, registry, serializer);

            // Compose TOM database
            var composer = new ModelComposer(registry);
            foreach (var connector in connectorConfigs)
                composer.RegisterConnector(connector);

            var database = composer.ComposeModel(modelDef, lineageService, projectPath, environment);

            // Validate TOM model
            var tomValidationResult = validator.ValidateTomModel(database, modelDef.Name);
            if (!tomValidationResult.IsValid)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(tomValidationResult.FormatMessages());
                Console.ResetColor();
                throw new InvalidOperationException($"TOM model validation failed for '{modelDef.Name}'");
            }

            if (tomValidationResult.HasWarnings)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(tomValidationResult.FormatMessages());
                Console.ResetColor();
                Console.WriteLine();
            }

            models.Add((database, modelFileName, modelDef.Name));
        }

        // Display lineage collision warnings if any
        if (lineageService?.CollisionWarnings.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (var warning in lineageService.CollisionWarnings)
            {
                Console.WriteLine(warning);
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        return models;
    }

    /// <summary>
    /// Load a named environment configuration
    /// </summary>
    private static EnvironmentDefinition LoadEnvironment(string projectPath, string envName, YamlSerializer serializer)
    {
        var envDir = Path.Combine(projectPath, "environments");
        var envFile = Path.Combine(envDir, $"{envName}.env.yml");

        if (!File.Exists(envFile))
        {
            // Try without .env suffix
            envFile = Path.Combine(envDir, $"{envName}.yml");
        }

        if (!File.Exists(envFile))
        {
            throw new FileNotFoundException(
                $"Environment '{envName}' not found. Expected file: {Path.Combine(envDir, $"{envName}.env.yml")}");
        }

        return serializer.LoadFromFile<EnvironmentDefinition>(envFile);
    }

    private static void GenerateTmdlOutput(Database database, string projectName, string targetPath, LineageManifestService? lineageService)
    {
        var sanitizedName = FileNameSanitizer.Sanitize(projectName);
        var modelOutputPath = Path.Combine(targetPath, sanitizedName);

        // Recreate output directory (clean build)
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

        if (lineageService != null)
        {
            Console.WriteLine($"  Lineage tags: {lineageService.NewTagCount} new, {lineageService.ExistingTagCount} existing");
        }

        Console.WriteLine();
    }

    private static void GeneratePbipOutput(Database database, string projectName, string targetPath, LineageManifestService? lineageService)
    {
        Console.WriteLine("Generating PBIP structure:");

        PbipGenerator.GeneratePbipStructure(database, projectName, targetPath);

        // Sanitize project name for display (same logic as in PbipGenerator)
        var sanitizedName = FileNameSanitizer.Sanitize(projectName);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ Created: {Path.Combine(targetPath, $"{sanitizedName}.pbip")}");
        Console.WriteLine($"  ✓ Created: {Path.Combine(targetPath, $"{sanitizedName}.SemanticModel/")} (TMDL files)");
        Console.WriteLine($"  ✓ Created: {Path.Combine(targetPath, $"{sanitizedName}.Report/")} (PBIR files)");
        Console.ResetColor();

        if (lineageService != null)
        {
            Console.WriteLine($"  Lineage tags: {lineageService.NewTagCount} new, {lineageService.ExistingTagCount} existing");
        }

        // Validate PBIP structure
        var validationErrors = PbipGenerator.ValidatePbipStructure(targetPath, projectName);
        if (validationErrors.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("PBIP validation errors:");
            foreach (var error in validationErrors)
            {
                Console.WriteLine($"  ✗ {error}");
            }
            Console.ResetColor();
            throw new InvalidOperationException($"PBIP structure validation failed with {validationErrors.Count} error(s)");
        }

        Console.WriteLine();
    }

    private static void SaveLineageManifest(LineageManifestService? lineageService, string projectPath)
    {
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
    }

    #endregion

    /// <summary>
    /// Scan for source type config files and extract connector configurations
    /// </summary>
    private static List<ConnectorConfig> ScanForConnectorConfigs(string projectPath, TableRegistry registry, YamlSerializer serializer)
    {
        var connectors = new List<ConnectorConfig>();
        var connectorNames = new HashSet<string>();

        // Check for source type config files (snowflake_config.yaml, sqlserver_config.yaml, etc.)
        var configFiles = Directory.GetFiles(projectPath, "*_config.yaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".pbiscaffold") && !f.Contains("scaffold_config"));

        foreach (var configFile in configFiles)
        {
            try
            {
                var sourceConfig = serializer.LoadFromFile<SourceTypeConfig>(configFile);
                if (sourceConfig.Connector != null && !connectorNames.Contains(sourceConfig.Connector.Name))
                {
                    connectors.Add(sourceConfig.Connector);
                    connectorNames.Add(sourceConfig.Connector.Name);
                }
            }
            catch (YamlDotNet.Core.YamlException)
            {
                // Skip files that cannot be parsed as SourceTypeConfig —
                // the *_config.yaml glob may match unrelated config files.
            }
            catch (IOException)
            {
                // Skip unreadable config files
            }
        }

        return connectors;
    }
}
