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
        command.AddCommand(CreateSourceSubcommand());
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
        var pathArgument = new Argument<string>("path", "Path to CSV schema file or TMDL folder/file");
        var outputPathArgument = new Argument<string>("output-path", () => "./tables", "Path where table YAML files will be created");
        var sourceConfigOption = new Option<string?>("--source-config", "Path to source configuration file (required for CSV imports)");
        var includeLineageTagsOption = new Option<bool>("--include-lineage-tags", "Preserve original lineage tags (TMDL imports only)");

        var command = new Command("table", "Import tables from CSV or TMDL to YAML format")
        {
            pathArgument, outputPathArgument, sourceConfigOption, includeLineageTagsOption
        };

        command.SetHandler((path, outputPath, sourceConfigPath, includeLineageTags) =>
        {
            try
            {
                if (IsCsvPath(path))
                {
                    if (string.IsNullOrEmpty(sourceConfigPath))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\n✗ Error: --source-config is required for CSV imports");
                        Console.ResetColor();
                        Console.WriteLine("\nUsage: pbt import table <csv-path> --source-config <config-path> [output-path]");
                        Environment.Exit(1);
                    }

                    if (includeLineageTags)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Warning: --include-lineage-tags is ignored for CSV imports");
                        Console.ResetColor();
                    }

                    ExecuteTableImportCsv(path, outputPath, sourceConfigPath);
                }
                else if (IsTmdlPath(path))
                {
                    if (!string.IsNullOrEmpty(sourceConfigPath))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Warning: --source-config is ignored for TMDL imports");
                        Console.ResetColor();
                    }

                    ExecuteTableImportTmdl(path, outputPath, includeLineageTags);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unable to determine file type for: {path}\n" +
                        "Expected either:\n" +
                        "  - CSV file (.csv extension)\n" +
                        "  - TMDL directory or file");
                }
            }
            catch (Exception ex)
            {
                PrintError("Import failed", ex);
                Environment.Exit(1);
            }
        }, pathArgument, outputPathArgument, sourceConfigOption, includeLineageTagsOption);

        return command;
    }

    private static void ExecuteTableImportCsv(string csvPath, string outputPath, string sourceConfigPath)
    {
        Console.WriteLine($"Importing tables from CSV: {csvPath}");
        Console.WriteLine($"Source config: {sourceConfigPath}");
        Console.WriteLine();

        var serializer = new YamlSerializer();

        if (!File.Exists(sourceConfigPath))
            throw new FileNotFoundException($"Source config file not found: {sourceConfigPath}");

        var sourceTypeConfig = serializer.LoadFromFile<SourceTypeConfig>(sourceConfigPath);
        ValidateSourceTypeConfig(sourceTypeConfig, sourceConfigPath);

        var config = ScaffoldConfig.CreateDefault();
        if (sourceTypeConfig.Connector == null)
            throw new InvalidOperationException($"Source config '{sourceConfigPath}' must include a 'connector' section");

        config.Source = new SourceConfig
        {
            Type = sourceTypeConfig.SourceType,
            Connection = sourceTypeConfig.Connector.Connection
        };

        Directory.CreateDirectory(outputPath);

        // Read CSV and generate tables
        var reader = new CsvSchemaReader();
        var rows = reader.ReadSchema(csvPath);
        var tableGroups = reader.GroupByTable(rows);

        Console.WriteLine($"Found {tableGroups.Count} table(s) with {rows.Count} column(s)");
        Console.WriteLine();

        var generator = new TableGenerator(config, sourceTypeConfig);
        var merger = new TableMerger(new MergeOptions { UpdateTypes = true });

        Console.WriteLine("Importing tables:");
        foreach (var (tableName, tableRows) in tableGroups)
        {
            var generated = generator.GenerateTable(tableName, tableRows);
            var fileName = FileNameSanitizer.SanitizeToLower(generated.Name) + ".yaml";
            var filePath = Path.Combine(outputPath, fileName);

            var merged = merger.MergeTable(generated, filePath);
            serializer.SaveToFile(merged, filePath);
            Console.WriteLine($"  ✓ {fileName} ({merged.Columns.Count} columns)");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Imported {tableGroups.Count} table(s)");
        Console.ResetColor();
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

    #region Source Subcommand

    private static Command CreateSourceSubcommand()
    {
        var sourceConfigArgument = new Argument<string>("source-config", "Path to source configuration file (e.g., snowflake_config.yaml)");
        var outputPathOption = new Option<string>("--output", () => "./tables", "Path where table YAML files will be created");
        var testConnectionOption = new Option<bool>("--test", "Test the connection without importing");
        var dryRunOption = new Option<bool>("--dry-run", "Show what would be imported without writing files");

        var command = new Command("source", "Import tables directly from a data source (Snowflake, SQL Server)")
        {
            sourceConfigArgument, outputPathOption, testConnectionOption, dryRunOption
        };

        command.SetHandler((sourceConfigPath, outputPath, testConnection, dryRun) =>
        {
            try
            {
                ExecuteSourceImport(sourceConfigPath, outputPath, testConnection, dryRun);
            }
            catch (Exception ex)
            {
                PrintError("Source import failed", ex);
                Environment.Exit(1);
            }
        }, sourceConfigArgument, outputPathOption, testConnectionOption, dryRunOption);

        return command;
    }

    private static void ExecuteSourceImport(string sourceConfigPath, string outputPath, bool testConnection, bool dryRun)
    {
        if (!File.Exists(sourceConfigPath))
            throw new FileNotFoundException($"Source config file not found: {sourceConfigPath}");

        var serializer = new YamlSerializer();
        var sourceConfig = serializer.LoadFromFile<SourceTypeConfig>(sourceConfigPath);

        if (sourceConfig.Import == null)
            throw new InvalidOperationException(
                $"Source config '{sourceConfigPath}' is missing 'import' section.\n" +
                "Add database, schema, and tables to enable live import.");

        Console.WriteLine($"Source: {sourceConfig.SourceType}");
        Console.WriteLine($"Database: {sourceConfig.Import.Database}");
        Console.WriteLine($"Schema: {sourceConfig.Import.Schema}");
        if (sourceConfig.Import.ImportAllTables)
            Console.WriteLine("Tables: all");
        else
            Console.WriteLine($"Tables: {string.Join(", ", sourceConfig.Import.Tables)}");
        Console.WriteLine();

        // Create the appropriate schema reader based on source type
        ISchemaReader reader = sourceConfig.SourceType.ToLowerInvariant() switch
        {
            "snowflake" => new SnowflakeSchemaReader(sourceConfig),
            _ => throw new InvalidOperationException(
                $"Live import not supported for source type '{sourceConfig.SourceType}'. " +
                "Supported: snowflake. For other sources, use 'pbt import table <csv-path>'.")
        };

        // Test connection mode
        if (testConnection)
        {
            Console.WriteLine("Testing connection...");
            if (reader is SnowflakeSchemaReader sfReader)
            {
                var info = sfReader.TestConnection();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ {info}");
                Console.ResetColor();
            }
            return;
        }

        // Read schema from live source
        Console.WriteLine("Querying INFORMATION_SCHEMA...");
        var rows = reader.ReadSchema();
        var csvReader = new CsvSchemaReader();
        var tableGroups = csvReader.GroupByTable(rows);

        Console.WriteLine($"Found {tableGroups.Count} table(s) with {rows.Count} column(s)");
        Console.WriteLine();

        if (dryRun)
        {
            Console.WriteLine("Tables that would be imported:");
            foreach (var (tableName, tableRows) in tableGroups)
                Console.WriteLine($"  • {tableName} ({tableRows.Count} columns)");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nDry run — no files written.");
            Console.ResetColor();
            return;
        }

        // Generate tables using existing pipeline
        Directory.CreateDirectory(outputPath);

        var config = ScaffoldConfig.CreateDefault();
        if (sourceConfig.Connector != null)
        {
            config.Source = new SourceConfig
            {
                Type = sourceConfig.SourceType,
                Connection = sourceConfig.Connector.Connection
            };
        }

        var generator = new TableGenerator(config, sourceConfig);
        var merger = new TableMerger(new MergeOptions { UpdateTypes = true });

        Console.WriteLine("Importing tables:");
        foreach (var (tableName, tableRows) in tableGroups)
        {
            var generated = generator.GenerateTable(tableName, tableRows);
            var fileName = FileNameSanitizer.SanitizeToLower(generated.Name) + ".yaml";
            var filePath = Path.Combine(outputPath, fileName);

            var merged = merger.MergeTable(generated, filePath);
            serializer.SaveToFile(merged, filePath);
            Console.WriteLine($"  ✓ {fileName} ({merged.Columns.Count} columns)");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Imported {tableGroups.Count} table(s) from {sourceConfig.SourceType}");
        Console.ResetColor();
    }

    #endregion

    #region Helpers

    private static bool IsCsvPath(string path) =>
        Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);

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

    private static void ValidateSourceTypeConfig(SourceTypeConfig config, string configPath)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.SourceType))
            errors.Add("'source_type' field is required (e.g., 'snowflake', 'sqlserver')");
        else
        {
            var supportedTypes = new[] { "snowflake", "sqlserver" };
            if (!supportedTypes.Contains(config.SourceType.ToLowerInvariant()))
                errors.Add($"'source_type' must be one of: {string.Join(", ", supportedTypes)}. Got: '{config.SourceType}'");
        }

        if (config.Connector == null)
            errors.Add("'connector' section is required");
        else
        {
            if (string.IsNullOrWhiteSpace(config.Connector.Name))
                errors.Add("'connector.name' field is required");
            if (string.IsNullOrWhiteSpace(config.Connector.Connection))
                errors.Add("'connector.connection' field is required");
            if (config.SourceType?.Equals("snowflake", StringComparison.OrdinalIgnoreCase) == true
                && string.IsNullOrWhiteSpace(config.Connector.Warehouse))
                errors.Add("'connector.warehouse' field is required for Snowflake sources");
        }

        if (errors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nSource config validation failed: {configPath}");
            foreach (var error in errors)
                Console.WriteLine($"  - {error}");
            Console.ResetColor();
            throw new InvalidOperationException("Source configuration is incomplete.");
        }
    }

    private static void ReportUnsupportedObjects(Database database, string mode)
    {
        var unsupported = new List<string>();
        if (database.Model.Perspectives.Count > 0)
            unsupported.Add($"Perspectives: {database.Model.Perspectives.Count}");
        if (database.Model.Roles.Count > 0)
            unsupported.Add($"Roles: {database.Model.Roles.Count}");
        foreach (var table in database.Model.Tables)
        {
            if (table.CalculationGroup != null)
                unsupported.Add($"Calculation Group: {table.Name}");
        }
        if (database.Model.Cultures.Count > 0)
            unsupported.Add($"Translations/Cultures: {database.Model.Cultures.Count}");

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
