using System.CommandLine;
using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Commands;

public static class ImportCommand
{
    public static Command Create()
    {
        var command = new Command("import", "Import TMDL models or tables to YAML format");
        command.AddCommand(CreateModelSubcommand());
        command.AddCommand(CreateTableSubcommand());
        return command;
    }

    #region Model Subcommand

    private static Command CreateModelSubcommand()
    {
        var tmdlPathArgument = new Argument<string>("tmdl-path", "Path to TMDL folder");
        var outputPathArgument = new Argument<string>("output-path", () => ".", "Path where YAML project will be created");
        var includeLineageTagsOption = new Option<bool>("--include-lineage-tags", "Preserve original lineage tags");
        var overwriteOption = new Option<bool>("--overwrite", "Overwrite existing files");
        var unsupportedObjectsOption = new Option<string>("--unsupported-objects", () => "warn", "How to handle unsupported TMDL constructs: warn, error, or skip");
        var showChangesOption = new Option<bool>("--show-changes", "Show diff of changes before applying");
        var autoMergeOption = new Option<bool>("--auto-merge", "Automatically merge changes without confirmation");

        var command = new Command("model", "Import TMDL model to YAML project structure")
        {
            tmdlPathArgument, outputPathArgument, includeLineageTagsOption,
            overwriteOption, unsupportedObjectsOption, showChangesOption, autoMergeOption
        };

        command.SetHandler((tmdlPath, outputPath, includeLineageTags, overwrite, unsupportedObjects, showChanges, autoMerge) =>
        {
            try
            {
                ExecuteModelImport(tmdlPath, outputPath, includeLineageTags, overwrite, unsupportedObjects);
            }
            catch (Exception ex)
            {
                PrintError("Import failed", ex);
                Environment.Exit(1);
            }
        }, tmdlPathArgument, outputPathArgument, includeLineageTagsOption, overwriteOption, unsupportedObjectsOption, showChangesOption, autoMergeOption);

        return command;
    }

    private static void ExecuteModelImport(string tmdlPath, string outputPath, bool includeLineageTags, bool overwrite, string unsupportedObjects = "warn")
    {
        Console.WriteLine($"Importing TMDL from: {tmdlPath}");
        Console.WriteLine($"Output to: {outputPath}");
        Console.WriteLine();

        if (!Directory.Exists(tmdlPath))
            throw new DirectoryNotFoundException($"TMDL directory not found: {tmdlPath}");

        ValidateOutputDirectory(outputPath, overwrite);

        // Load TMDL
        Console.WriteLine("Loading TMDL model...");
        var database = TmdlSerializer.DeserializeDatabaseFromFolder(tmdlPath);

        if (database.Model == null)
            throw new InvalidOperationException("TMDL does not contain a valid model");

        Console.WriteLine($"Model: {database.Name}");
        Console.WriteLine($"  Tables: {database.Model.Tables.Count}");
        Console.WriteLine($"  Relationships: {database.Model.Relationships.Count}");
        Console.WriteLine();

        // Check unsupported objects
        ReportUnsupportedObjects(database, unsupportedObjects);

        // Create output structure
        var tablesPath = Path.Combine(outputPath, "tables");
        var modelsPath = Path.Combine(outputPath, "models");
        Directory.CreateDirectory(tablesPath);
        Directory.CreateDirectory(modelsPath);
        Directory.CreateDirectory(Path.Combine(outputPath, ".pbt"));

        var serializer = new YamlSerializer();

        // Extract tables using TomConverter
        Console.WriteLine($"\nExtracting {database.Model.Tables.Count} tables:");
        foreach (var table in database.Model.Tables)
        {
            var tableDef = TomConverter.ToTableDefinition(table, includeLineageTags);
            var fileName = FileNameSanitizer.SanitizeToLower(table.Name) + ".yaml";
            serializer.SaveToFile(tableDef, Path.Combine(tablesPath, fileName));
            Console.WriteLine($"  • {table.Name} -> {fileName}");
        }

        // Extract model using TomConverter
        Console.WriteLine($"\nExtracting model definition:");
        var modelDef = TomConverter.ToModelDefinition(database, includeLineageTags);
        var modelFileName = FileNameSanitizer.SanitizeToLower(database.Name) + "_model.yaml";
        serializer.SaveToFile(modelDef, Path.Combine(modelsPath, modelFileName));
        Console.WriteLine($"  • {database.Name} -> {modelFileName}");

        // Create .gitignore
        File.WriteAllText(Path.Combine(outputPath, ".gitignore"), """
            # Build output
            target/

            # Lineage manifest (optional - remove if you want to track lineage tags in git)
            .pbt/lineage.yaml

            # Temp files
            *.tmp
            *.bak
            """.Replace("            ", ""));

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Import completed successfully");
        Console.ResetColor();

        if (!includeLineageTags)
        {
            Console.WriteLine();
            Console.WriteLine("Note: Lineage tags were not included. Run 'pbt build' to generate new lineage tags.");
        }
    }

    #endregion

    #region Table Subcommand

    private static Command CreateTableSubcommand()
    {
        var pathArgument = new Argument<string>("path", "Path to TMDL folder/file (generate .tmdl from CSV/Snowflake with the plugins in plugins/)");
        var outputPathArgument = new Argument<string>("output-path", () => "./tables", "Path where table YAML files will be created");
        var includeLineageTagsOption = new Option<bool>("--include-lineage-tags", "Preserve original lineage tags");

        var command = new Command("table", "Import tables from TMDL to YAML format")
        {
            pathArgument, outputPathArgument, includeLineageTagsOption
        };

        command.SetHandler((path, outputPath, includeLineageTags) =>
        {
            try
            {
                if (!IsTmdlPath(path))
                    throw new InvalidOperationException(
                        $"Not a TMDL directory or .tmdl file: {path}\n" +
                        "To start from a CSV schema or Snowflake, generate .tmdl with plugins/csv_to_tmdl or plugins/snowflake_to_tmdl.");

                ExecuteTableImportTmdl(path, outputPath, includeLineageTags);
            }
            catch (Exception ex)
            {
                PrintError("Import failed", ex);
                Environment.Exit(1);
            }
        }, pathArgument, outputPathArgument, includeLineageTagsOption);

        return command;
    }

    private static void ExecuteTableImportTmdl(string tmdlPath, string outputPath, bool includeLineageTags)
    {
        Console.WriteLine($"Importing tables from TMDL: {tmdlPath}");
        Console.WriteLine();

        if (!Directory.Exists(tmdlPath) && !File.Exists(tmdlPath))
            throw new FileNotFoundException($"TMDL path not found: {tmdlPath}");

        Directory.CreateDirectory(outputPath);

        var serializer = new YamlSerializer();
        var importer = new TmdlTableImporter(serializer);
        var merger = new TableMerger(new MergeOptions { UpdateTypes = true });

        Console.WriteLine("Loading TMDL model...");
        var tables = importer.ExtractTables(tmdlPath, includeLineageTags);
        Console.WriteLine($"Found {tables.Count} table(s)");
        Console.WriteLine();

        Console.WriteLine("Importing tables:");
        foreach (var table in tables)
        {
            var fileName = FileNameSanitizer.SanitizeToLower(table.Name) + ".yaml";
            var filePath = Path.Combine(outputPath, fileName);

            var merged = merger.MergeTable(table, filePath);
            serializer.SaveToFile(merged, filePath);
            Console.WriteLine($"  ✓ {fileName} ({merged.Columns.Count} columns)");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Imported {tables.Count} table(s)");
        Console.ResetColor();

        if (!includeLineageTags)
        {
            Console.WriteLine();
            Console.WriteLine("Note: Lineage tags were not included. Run 'pbt build' to generate new lineage tags.");
        }
    }

    #endregion

    #region Helpers

    private static bool IsTmdlPath(string path)
    {
        if (Directory.Exists(path))
            return Directory.GetFiles(path, "*.tmdl", SearchOption.AllDirectories).Length > 0;
        return File.Exists(path) && Path.GetExtension(path).Equals(".tmdl", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateOutputDirectory(string outputPath, bool overwrite)
    {
        if (Directory.Exists(outputPath))
        {
            if (!overwrite)
            {
                var files = Directory.GetFiles(outputPath);
                var dirs = Directory.GetDirectories(outputPath);
                if (files.Length > 0 || dirs.Length > 0)
                    throw new InvalidOperationException($"Output directory '{outputPath}' is not empty. Use --overwrite to overwrite existing files.");
            }
        }
        else
        {
            Directory.CreateDirectory(outputPath);
        }
    }

    private static void ReportUnsupportedObjects(Database database, string mode)
    {
        var unsupported = new List<string>();
        foreach (var table in database.Model.Tables.Where(t => t.Calendars.Count > 0))
            unsupported.Add($"Calendars on table {table.Name}: {table.Calendars.Count}");

        if (unsupported.Count == 0) return;

        var message = "Unsupported TMDL constructs found:\n  " + string.Join("\n  ", unsupported);

        switch (mode.ToLowerInvariant())
        {
            case "error":
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(message);
                Console.ResetColor();
                throw new InvalidOperationException("Import aborted due to unsupported objects (--unsupported-objects error)");
            case "skip":
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"Skipping unsupported objects: {string.Join(", ", unsupported)}");
                Console.ResetColor();
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARNING: " + message);
                Console.WriteLine("These objects will not be included in the import.");
                Console.ResetColor();
                break;
        }
        Console.WriteLine();
    }

    private static void PrintError(string context, Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n✗ {context}: {ex.Message}");
        Console.ResetColor();
    }

    #endregion
}
