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

        // Add subcommands
        command.AddCommand(CreateModelSubcommand());
        command.AddCommand(CreateTableSubcommand());

        return command;
    }

    #region Model Subcommand

    private static Command CreateModelSubcommand()
    {
        var tmdlPathArgument = new Argument<string>(
            "tmdl-path",
            "Path to TMDL folder");

        var outputPathArgument = new Argument<string>(
            "output-path",
            () => ".",
            "Path where YAML project will be created (defaults to current directory)");

        var includeLineageTagsOption = new Option<bool>(
            "--include-lineage-tags",
            "Preserve original lineage tags");

        var overwriteOption = new Option<bool>(
            "--overwrite",
            "Overwrite existing files");

        var unsupportedObjectsOption = new Option<string>(
            "--unsupported-objects",
            () => "warn",
            "How to handle unsupported TMDL constructs: warn, error, or skip");

        var showChangesOption = new Option<bool>(
            "--show-changes",
            "Show diff of changes before applying (requires confirmation)");

        var autoMergeOption = new Option<bool>(
            "--auto-merge",
            "Automatically merge changes without confirmation (default behavior for backward compatibility)");

        var command = new Command("model", "Import TMDL model to YAML project structure")
        {
            tmdlPathArgument,
            outputPathArgument,
            includeLineageTagsOption,
            overwriteOption,
            unsupportedObjectsOption,
            showChangesOption,
            autoMergeOption
        };

        command.SetHandler((tmdlPath, outputPath, includeLineageTags, overwrite, unsupportedObjects, showChanges, autoMerge) =>
        {
            try
            {
                ExecuteModelImport(tmdlPath, outputPath, includeLineageTags, overwrite, unsupportedObjects, showChanges);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Import failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, tmdlPathArgument, outputPathArgument, includeLineageTagsOption, overwriteOption, unsupportedObjectsOption, showChangesOption, autoMergeOption);

        return command;
    }

    // Known supported TMDL constructs
    private static readonly HashSet<string> SupportedConstructs = new()
    {
        "Table", "Column", "DataColumn", "CalculatedColumn", "Measure",
        "Hierarchy", "Level", "Partition", "MPartitionSource",
        "SingleColumnRelationship", "NamedExpression"
    };

    // Unsupported constructs that are logged
    private static readonly HashSet<string> UnsupportedConstructs = new()
    {
        "CalculationGroup", "CalculationItem", "Perspective",
        "ModelRole", "TablePermission", "Translation", "Culture",
        "ObjectLevelSecurity", "KPI"
    };

    private static void ExecuteModelImport(string tmdlPath, string outputPath, bool includeLineageTags, bool overwrite, string unsupportedObjects = "warn", bool showChanges = false)
    {
        Console.WriteLine($"Importing TMDL from: {tmdlPath}");
        Console.WriteLine($"Output to: {outputPath}");
        Console.WriteLine($"Unsupported objects: {unsupportedObjects}");
        Console.WriteLine();

        // Validate TMDL path
        if (!Directory.Exists(tmdlPath))
        {
            throw new DirectoryNotFoundException($"TMDL directory not found: {tmdlPath}");
        }

        // Check output path
        if (Directory.Exists(outputPath))
        {
            if (!overwrite)
            {
                var files = Directory.GetFiles(outputPath);
                var dirs = Directory.GetDirectories(outputPath);
                if (files.Length > 0 || dirs.Length > 0)
                {
                    throw new InvalidOperationException($"Output directory '{outputPath}' is not empty. Use --overwrite to overwrite existing files.");
                }
            }
        }
        else
        {
            Directory.CreateDirectory(outputPath);
        }

        // Load TMDL
        Console.WriteLine("Loading TMDL model...");
        var database = TmdlSerializer.DeserializeDatabaseFromFolder(tmdlPath);

        if (database.Model == null)
        {
            throw new InvalidOperationException("TMDL does not contain a valid model");
        }

        Console.WriteLine($"Model: {database.Name}");
        Console.WriteLine($"  Tables: {database.Model.Tables.Count}");
        Console.WriteLine($"  Relationships: {database.Model.Relationships.Count}");
        Console.WriteLine();

        // Check for unsupported objects
        var unsupportedFound = new List<string>();
        if (database.Model.Perspectives.Count > 0)
            unsupportedFound.Add($"Perspectives: {database.Model.Perspectives.Count}");
        if (database.Model.Roles.Count > 0)
            unsupportedFound.Add($"Roles: {database.Model.Roles.Count}");
        foreach (var table in database.Model.Tables)
        {
            if (table.CalculationGroup != null)
                unsupportedFound.Add($"Calculation Group: {table.Name}");
        }
        if (database.Model.Cultures.Count > 0)
            unsupportedFound.Add($"Translations/Cultures: {database.Model.Cultures.Count}");

        if (unsupportedFound.Count > 0)
        {
            var message = $"Unsupported TMDL constructs found:\n  " + string.Join("\n  ", unsupportedFound);

            switch (unsupportedObjects.ToLowerInvariant())
            {
                case "error":
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(message);
                    Console.ResetColor();
                    throw new InvalidOperationException("Import aborted due to unsupported objects (--unsupported-objects error)");
                case "skip":
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"Skipping unsupported objects: {string.Join(", ", unsupportedFound)}");
                    Console.ResetColor();
                    Console.WriteLine();
                    break;
                case "warn":
                default:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WARNING: " + message);
                    Console.WriteLine("These objects will not be included in the import.");
                    Console.ResetColor();
                    Console.WriteLine();
                    break;
            }
        }

        // Create output directory structure
        var tablesPath = Path.Combine(outputPath, "tables");
        var modelsPath = Path.Combine(outputPath, "models");

        Directory.CreateDirectory(tablesPath);
        Directory.CreateDirectory(modelsPath);
        Directory.CreateDirectory(Path.Combine(outputPath, ".pbt"));

        var serializer = new YamlSerializer();

        // Create project.yml with assets config so 'pbt build' works immediately
        var project = new ProjectDefinition
        {
            Name = database.Name,
            Description = $"Imported from TMDL: {Path.GetFileName(tmdlPath)}",
            CompatibilityLevel = database.CompatibilityLevel,
            Assets = new Dictionary<string, List<AssetPathConfig>>
            {
                ["project"] = new List<AssetPathConfig>
                {
                    new AssetPathConfig { Path = "." }
                }
            }
        };
        serializer.SaveToFile(project, Path.Combine(outputPath, "project.yml"));
        Console.WriteLine("Created project.yml");

        // Extract tables
        Console.WriteLine($"\nExtracting {database.Model.Tables.Count} tables:");
        foreach (var table in database.Model.Tables)
        {
            var tableDef = ExtractTableDefinition(table, includeLineageTags);
            var fileName = FileNameSanitizer.SanitizeToLower(table.Name) + ".yaml";
            serializer.SaveToFile(tableDef, Path.Combine(tablesPath, fileName));
            Console.WriteLine($"  • {table.Name} -> {fileName}");
        }

        // Extract model
        Console.WriteLine($"\nExtracting model definition:");
        var modelDef = ExtractModelDefinition(database, includeLineageTags);
        var modelFileName = FileNameSanitizer.SanitizeToLower(database.Name) + "_model.yaml";
        serializer.SaveToFile(modelDef, Path.Combine(modelsPath, modelFileName));
        Console.WriteLine($"  • {database.Name} -> {modelFileName}");

        // Create .gitignore
        var gitignoreContent = @"# Build output
target/

# Lineage manifest (optional - remove if you want to track lineage tags in git)
.pbt/lineage.yaml

# Temp files
*.tmp
*.bak
";
        File.WriteAllText(Path.Combine(outputPath, ".gitignore"), gitignoreContent);

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
        var pathArgument = new Argument<string>(
            "path",
            "Path to CSV schema file or TMDL folder/file");

        var outputPathArgument = new Argument<string>(
            "output-path",
            () => "./tables",
            "Path where table YAML files will be created (defaults to ./tables)");

        var sourceConfigOption = new Option<string?>(
            "--source-config",
            "Path to source configuration file (required for CSV imports)");

        var includeLineageTagsOption = new Option<bool>(
            "--include-lineage-tags",
            "Preserve original lineage tags (TMDL imports only)");

        var command = new Command("table", "Import tables from CSV or TMDL to YAML format")
        {
            pathArgument,
            outputPathArgument,
            sourceConfigOption,
            includeLineageTagsOption
        };

        command.SetHandler((path, outputPath, sourceConfigPath, includeLineageTags) =>
        {
            try
            {
                // Detect path type
                if (IsCsvPath(path))
                {
                    // CSV import - validate source-config is provided
                    if (string.IsNullOrEmpty(sourceConfigPath))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\n✗ Error: --source-config is required for CSV imports");
                        Console.ResetColor();
                        Console.WriteLine("\nUsage: pbt import table <csv-path> --source-config <config-path> [output-path]");
                        Console.WriteLine("\nExample:");
                        Console.WriteLine("  pbt import table schema.csv --source-config snowflake_config.yaml");
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
                    // TMDL import
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Import failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, pathArgument, outputPathArgument, sourceConfigOption, includeLineageTagsOption);

        return command;
    }

    private static void ExecuteTableImportCsv(string csvPath, string outputPath, string sourceConfigPath)
    {
        Console.WriteLine($"Importing tables from CSV: {csvPath}");
        Console.WriteLine($"Output: {outputPath}");
        Console.WriteLine($"Source config: {sourceConfigPath}");
        Console.WriteLine();

        // Initialize YAML serializer
        var serializer = new YamlSerializer();

        // Load source-specific type configuration
        if (!File.Exists(sourceConfigPath))
        {
            throw new FileNotFoundException($"Source config file not found: {sourceConfigPath}");
        }

        Console.WriteLine($"Loading source type config from: {sourceConfigPath}");
        var sourceTypeConfig = serializer.LoadFromFile<SourceTypeConfig>(sourceConfigPath);

        // Validate source type config has required fields
        ValidateSourceTypeConfig(sourceTypeConfig, sourceConfigPath);

        // Create scaffold config with source information
        var config = ScaffoldConfig.CreateDefault();

        if (sourceTypeConfig.Connector != null)
        {
            config.Source = new SourceConfig
            {
                Type = sourceTypeConfig.SourceType,
                Connection = sourceTypeConfig.Connector.Connection
            };
        }
        else
        {
            throw new InvalidOperationException(
                $"Source config file '{sourceConfigPath}' must include a 'connector' section with connection information");
        }

        // Create output directory
        Directory.CreateDirectory(outputPath);

        // Read CSV schema
        Console.WriteLine($"\nReading CSV schema...");
        var reader = new CsvSchemaReader();
        var rows = reader.ReadSchema(csvPath);
        var tableGroups = reader.GroupByTable(rows);

        Console.WriteLine($"Found {tableGroups.Count} table(s) with {rows.Count} column(s)");
        Console.WriteLine();

        // Initialize table generator with source type config
        var generator = new TableGenerator(config, sourceTypeConfig);

        // Configure smart merge
        var mergeOptions = new MergeOptions
        {
            DryRun = false,
            PruneDeleted = false,
            UpdateTypes = true,
            OverwriteDescriptions = false
        };
        var merger = new SmartMerger(mergeOptions);

        // Process each table
        Console.WriteLine($"Importing tables:");

        foreach (var (tableName, tableRows) in tableGroups)
        {
            // Generate table definition
            var generated = generator.GenerateTable(tableName, tableRows);
            var fileName = FileNameSanitizer.SanitizeToLower(generated.Name) + ".yaml";
            var filePath = Path.Combine(outputPath, fileName);

            // Merge with existing (if exists)
            var merged = merger.MergeTable(generated, filePath);

            // Write file
            serializer.SaveToFile(merged, filePath);
            Console.WriteLine($"  ✓ {fileName} ({merged.Columns.Count} columns)");
        }

        // Summary
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Import completed successfully");
        Console.ResetColor();
        Console.WriteLine($"  Imported {tableGroups.Count} table definition(s)");
        Console.WriteLine($"\nNext steps:");
        Console.WriteLine($"  1. Review imported YAML files in: {outputPath}");
        Console.WriteLine($"  2. Add to model definition to use in builds");
    }

    private static void ExecuteTableImportTmdl(string tmdlPath, string outputPath, bool includeLineageTags)
    {
        Console.WriteLine($"Importing tables from TMDL: {tmdlPath}");
        Console.WriteLine($"Output: {outputPath}");
        Console.WriteLine();

        // Validate TMDL path
        if (!Directory.Exists(tmdlPath) && !File.Exists(tmdlPath))
        {
            throw new FileNotFoundException($"TMDL path not found: {tmdlPath}");
        }

        // Create output directory
        Directory.CreateDirectory(outputPath);

        // Initialize services
        var serializer = new YamlSerializer();
        var importer = new TmdlTableImporter(serializer);
        var mergeOptions = new MergeOptions
        {
            DryRun = false,
            PruneDeleted = false,
            UpdateTypes = true,
            OverwriteDescriptions = false
        };
        var merger = new SmartMerger(mergeOptions);

        // Extract tables from TMDL
        Console.WriteLine("Loading TMDL model...");
        var tables = importer.ExtractTables(tmdlPath, includeLineageTags);

        Console.WriteLine($"Found {tables.Count} table(s)");
        Console.WriteLine();

        // Process each table with smart merge
        Console.WriteLine("Importing tables:");
        foreach (var table in tables)
        {
            var fileName = FileNameSanitizer.SanitizeToLower(table.Name) + ".yaml";
            var filePath = Path.Combine(outputPath, fileName);

            // Smart merge with existing (if exists)
            var merged = merger.MergeTable(table, filePath);

            // Write file
            serializer.SaveToFile(merged, filePath);
            Console.WriteLine($"  ✓ {fileName} ({merged.Columns.Count} columns)");
        }

        // Summary
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Import completed successfully");
        Console.ResetColor();
        Console.WriteLine($"  Imported {tables.Count} table definition(s)");

        if (!includeLineageTags)
        {
            Console.WriteLine();
            Console.WriteLine("Note: Lineage tags were not included. Run 'pbt build' to generate new lineage tags.");
        }

        Console.WriteLine($"\nNext steps:");
        Console.WriteLine($"  1. Review imported YAML files in: {outputPath}");
        Console.WriteLine($"  2. Add to model definition to use in builds");
    }

    #endregion

    #region Shared Helper Methods

    private static bool IsCsvPath(string path)
    {
        return Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTmdlPath(string path)
    {
        // TMDL can be either:
        // 1. A directory containing .tmdl files
        // 2. A .tmdl file itself
        if (Directory.Exists(path))
        {
            return Directory.GetFiles(path, "*.tmdl", SearchOption.AllDirectories).Length > 0;
        }

        return File.Exists(path) && Path.GetExtension(path).Equals(".tmdl", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateSourceTypeConfig(SourceTypeConfig config, string configPath)
    {
        var errors = new List<string>();

        // Validate source_type field
        if (string.IsNullOrWhiteSpace(config.SourceType))
        {
            errors.Add("'source_type' field is required (e.g., 'snowflake', 'sqlserver')");
        }
        else
        {
            // Validate it's a supported source type
            var supportedTypes = new[] { "snowflake", "sqlserver" };
            if (!supportedTypes.Contains(config.SourceType.ToLowerInvariant()))
            {
                errors.Add($"'source_type' must be one of: {string.Join(", ", supportedTypes)}. Got: '{config.SourceType}'");
            }
        }

        // Validate connector section exists
        if (config.Connector == null)
        {
            errors.Add("'connector' section is required");
        }
        else
        {
            // Validate connector fields
            if (string.IsNullOrWhiteSpace(config.Connector.Name))
            {
                errors.Add("'connector.name' field is required (e.g., 'SnowflakeSource')");
            }

            if (string.IsNullOrWhiteSpace(config.Connector.Connection))
            {
                errors.Add("'connector.connection' field is required (server address or connection string)");
            }

            // Source-specific validation
            if (config.SourceType?.Equals("snowflake", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (string.IsNullOrWhiteSpace(config.Connector.Warehouse))
                {
                    errors.Add("'connector.warehouse' field is required for Snowflake sources");
                }
            }
        }

        if (errors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nSource config validation failed: {configPath}");
            Console.WriteLine("\nRequired fields missing:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  - {error}");
            }
            Console.ResetColor();
            throw new InvalidOperationException("Source configuration is incomplete. See errors above.");
        }
    }

    private static TableDefinition ExtractTableDefinition(Table table, bool includeLineageTags)
    {
        var tableDef = new TableDefinition
        {
            Name = table.Name,
            Description = table.Description,
            IsHidden = table.IsHidden,
            LineageTag = includeLineageTags ? table.LineageTag : null,
            Columns = new List<ColumnDefinition>(),
            Hierarchies = new List<HierarchyDefinition>(),
            Measures = new List<MeasureDefinition>()
        };

        // Extract M expression from partition
        if (table.Partitions.Count > 0 && table.Partitions[0].Source is MPartitionSource mSource)
        {
            tableDef.MExpression = mSource.Expression;
        }

        // Extract data columns
        foreach (var column in table.Columns.OfType<DataColumn>())
        {
            var colDef = new ColumnDefinition
            {
                Name = column.Name,
                Type = column.DataType.ToString(),
                Description = column.Description,
                SourceColumn = column.SourceColumn,
                FormatString = column.FormatString,
                IsHidden = column.IsHidden,
                DisplayFolder = column.DisplayFolder,
                LineageTag = includeLineageTags ? column.LineageTag : null
            };

            tableDef.Columns.Add(colDef);
        }

        // Extract calculated columns
        foreach (var column in table.Columns.OfType<CalculatedColumn>())
        {
            var colDef = new ColumnDefinition
            {
                Name = column.Name,
                Type = column.DataType.ToString(),
                Description = column.Description,
                Expression = column.Expression,
                FormatString = column.FormatString,
                IsHidden = column.IsHidden,
                DisplayFolder = column.DisplayFolder,
                LineageTag = includeLineageTags ? column.LineageTag : null
            };

            tableDef.Columns.Add(colDef);
        }

        // Extract hierarchies
        foreach (var hierarchy in table.Hierarchies)
        {
            var hierarchyDef = new HierarchyDefinition
            {
                Name = hierarchy.Name,
                Description = hierarchy.Description,
                LineageTag = includeLineageTags ? hierarchy.LineageTag : null,
                Levels = new List<LevelDefinition>()
            };

            foreach (var level in hierarchy.Levels)
            {
                hierarchyDef.Levels.Add(new LevelDefinition
                {
                    Name = level.Name,
                    Column = level.Column.Name
                });
            }

            tableDef.Hierarchies.Add(hierarchyDef);
        }

        // Extract measures
        foreach (var measure in table.Measures)
        {
            var measureDef = new MeasureDefinition
            {
                Name = measure.Name,
                Table = table.Name,
                Expression = measure.Expression,
                Description = measure.Description,
                FormatString = measure.FormatString,
                DisplayFolder = measure.DisplayFolder,
                IsHidden = measure.IsHidden,
                LineageTag = includeLineageTags ? measure.LineageTag : null
            };

            tableDef.Measures.Add(measureDef);
        }

        return tableDef;
    }

    private static ModelDefinition ExtractModelDefinition(Database database, bool includeLineageTags)
    {
        var modelDef = new ModelDefinition
        {
            Name = database.Name,
            Description = database.Model.Description,
            Tables = new List<TableReference>(),
            Relationships = new List<RelationshipDefinition>(),
            Measures = new List<MeasureDefinition>()
        };

        // Add table references
        foreach (var table in database.Model.Tables)
        {
            modelDef.Tables.Add(new TableReference { Ref = table.Name });
        }

        // Extract relationships
        foreach (var relationship in database.Model.Relationships.OfType<SingleColumnRelationship>())
        {
            var relDef = new RelationshipDefinition
            {
                FromTable = relationship.FromTable.Name,
                FromColumn = relationship.FromColumn.Name,
                ToTable = relationship.ToTable.Name,
                ToColumn = relationship.ToColumn.Name,
                Cardinality = MapCardinality(relationship.FromCardinality, relationship.ToCardinality),
                CrossFilterDirection = MapCrossFilterDirection(relationship.CrossFilteringBehavior),
                Active = relationship.IsActive
            };

            modelDef.Relationships.Add(relDef);
        }

        // Extract measures from all tables
        foreach (var table in database.Model.Tables)
        {
            foreach (var measure in table.Measures)
            {
                var measureDef = new MeasureDefinition
                {
                    Name = measure.Name,
                    Table = table.Name,
                    Expression = measure.Expression,
                    Description = measure.Description,
                    FormatString = measure.FormatString,
                    DisplayFolder = measure.DisplayFolder,
                    LineageTag = includeLineageTags ? measure.LineageTag : null
                };

                modelDef.Measures.Add(measureDef);
            }
        }

        return modelDef;
    }

    private static string MapCardinality(RelationshipEndCardinality from, RelationshipEndCardinality to)
    {
        return (from, to) switch
        {
            (RelationshipEndCardinality.Many, RelationshipEndCardinality.One) => "ManyToOne",
            (RelationshipEndCardinality.One, RelationshipEndCardinality.Many) => "OneToMany",
            (RelationshipEndCardinality.One, RelationshipEndCardinality.One) => "OneToOne",
            (RelationshipEndCardinality.Many, RelationshipEndCardinality.Many) => "ManyToMany",
            _ => throw new InvalidOperationException($"Unknown cardinality combination: {from} to {to}")
        };
    }

    private static string MapCrossFilterDirection(CrossFilteringBehavior behavior)
    {
        return behavior switch
        {
            CrossFilteringBehavior.OneDirection => "Single",
            CrossFilteringBehavior.BothDirections => "Both",
            CrossFilteringBehavior.Automatic => "Automatic",
            _ => "Single"
        };
    }

    #endregion
}
