using System.CommandLine;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Commands;

public static class ValidateCommand
{
    public static Command Create()
    {
        var projectPathArgument = new Argument<string>(
            "project-path",
            () => ".",
            "Path to the project directory (defaults to current directory)");

        var verboseOption = new Option<bool>(
            "--verbose",
            "Show all checks performed");

        var strictOption = new Option<bool>(
            "--strict",
            "Treat warnings as errors");

        var command = new Command("validate", "Validate project configuration and definitions")
        {
            projectPathArgument,
            verboseOption,
            strictOption
        };

        command.SetHandler((projectPath, verbose, strict) =>
        {
            try
            {
                ExecuteValidate(projectPath, verbose, strict);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Validation error: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, projectPathArgument, verboseOption, strictOption);

        return command;
    }

    private static void ExecuteValidate(string projectPath, bool verbose, bool strict)
    {
        Console.WriteLine($"Validating project: {projectPath}");
        Console.WriteLine();

        // Validate project path
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        }

        var serializer = new YamlSerializer();
        var assetLoader = new AssetLoader(serializer);
        var validator = new Validator(serializer);

        if (verbose)
        {
            Console.WriteLine("Running validation checks:");
            Console.WriteLine("  - Project configuration (project.yml)");
            Console.WriteLine("  - Asset path resolution");
            Console.WriteLine("  - Table definitions");
            Console.WriteLine("  - Model definitions");
            Console.WriteLine("  - Relationships");
            Console.WriteLine("  - Measures");
            Console.WriteLine();
        }

        // Load project and resolve asset paths
        var (project, assetPaths) = assetLoader.LoadProject(projectPath);

        if (verbose)
        {
            Console.WriteLine("Resolved asset paths:");
            Console.WriteLine($"  Tables ({assetPaths.TablePaths.Count} paths):");
            foreach (var path in assetPaths.TablePaths)
            {
                Console.WriteLine($"    - {path}");
            }
            Console.WriteLine($"  Models ({assetPaths.ModelPaths.Count} paths):");
            foreach (var path in assetPaths.ModelPaths)
            {
                Console.WriteLine($"    - {path}");
            }
            Console.WriteLine($"  Macros ({assetPaths.MacroPaths.Count} paths):");
            foreach (var path in assetPaths.MacroPaths)
            {
                Console.WriteLine($"    - {path}");
            }
            Console.WriteLine();
        }

        // Run validation with resolved asset paths
        var result = validator.ValidateProjectWithAssets(projectPath, assetPaths);

        // Display results
        if (result.IsValid && !result.HasWarnings)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(result.FormatMessages());
            Console.ResetColor();
            return;
        }

        if (result.HasWarnings)
        {
            if (strict)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Validation failed (strict mode treats warnings as errors):");
                Console.WriteLine();
                Console.WriteLine(result.FormatMessages());
                Console.ResetColor();
                Environment.Exit(1);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(result.FormatMessages());
                Console.ResetColor();

                if (!result.IsValid)
                {
                    Console.WriteLine();
                    Environment.Exit(1);
                }
            }
        }

        if (!result.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.FormatMessages());
            Console.ResetColor();
            Environment.Exit(1);
        }

        if (verbose)
        {
            Console.WriteLine();
            Console.WriteLine("Summary:");
            Console.WriteLine($"  Errors: {result.Errors.Count}");
            Console.WriteLine($"  Warnings: {result.Warnings.Count}");
            Console.WriteLine($"  Total issues: {result.TotalIssues}");
        }
    }
}
