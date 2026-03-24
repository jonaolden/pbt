using System.CommandLine;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            "path",
            () => ".",
            "Path where the project will be created (defaults to current directory)");

        var examplesOption = new Option<bool>(
            "--examples",
            "Include example files in the project");

        var command = new Command("init", "Initialize a new PBI Composer project")
        {
            pathArgument,
            examplesOption
        };

        command.SetHandler((path, includeExamples) =>
        {
            try
            {
                ExecuteInit(path, includeExamples);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Initialization failed: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, pathArgument, examplesOption);

        return command;
    }

    private static void ExecuteInit(string path, bool includeExamples)
    {
        Console.WriteLine($"Initializing project at: {path}");
        Console.WriteLine();

        // Check if directory already exists
        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path);
            var dirs = Directory.GetDirectories(path);

            if (files.Length > 0 || dirs.Length > 0)
            {
                throw new InvalidOperationException($"Directory '{path}' is not empty. Please choose an empty directory or a new path.");
            }
        }
        else
        {
            Directory.CreateDirectory(path);
        }

        // Create directory structure
        Directory.CreateDirectory(Path.Combine(path, "tables"));
        Directory.CreateDirectory(Path.Combine(path, "models"));
        Directory.CreateDirectory(Path.Combine(path, ".pbt"));

        Console.WriteLine("Created directory structure:");
        Console.WriteLine("  tables/");
        Console.WriteLine("  models/");
        Console.WriteLine("  .pbt/");
        Console.WriteLine();

        var serializer = new YamlSerializer();

        // Create .gitignore
        var gitignoreContent = @"# Build output
target/

# Lineage manifest (optional - remove if you want to track lineage tags in git)
.pbt/lineage.yaml

# Temp files
*.tmp
*.bak
";
        File.WriteAllText(Path.Combine(path, ".gitignore"), gitignoreContent);
        Console.WriteLine("Created .gitignore");
        Console.WriteLine();

        // Create example files if requested
        if (includeExamples)
        {
            CreateExampleFiles(path, serializer);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Project initialized successfully");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  1. cd {path}");
        Console.WriteLine("  2. Add table definitions to tables/ directory");
        Console.WriteLine("  3. Create model definitions in models/ directory");
        Console.WriteLine("  4. Run: pbt build .");
    }

    private static void CreateExampleFiles(string projectPath, YamlSerializer serializer)
    {
        Console.WriteLine("Creating example files:");

        // Create example table - DimProduct
        var productTable = new TableDefinition
        {
            Name = "DimProduct",
            Description = "Product dimension table",
            MExpression = @"let
    Source = #table(
        {""ProductID"", ""ProductName"", ""Category"", ""Price""},
        {
            {1, ""Laptop"", ""Electronics"", 999.99},
            {2, ""Mouse"", ""Electronics"", 29.99},
            {3, ""Desk"", ""Furniture"", 299.99}
        }
    )
in
    Source",
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "ProductID", Type = "Int64", Description = "Unique product identifier" },
                new() { Name = "ProductName", Type = "String", Description = "Product name" },
                new() { Name = "Category", Type = "String", Description = "Product category" },
                new() { Name = "Price", Type = "Decimal", FormatString = "$#,##0.00" }
            }
        };

        serializer.SaveToFile(productTable, Path.Combine(projectPath, "tables", "dim_product.yaml"));
        Console.WriteLine("  Created tables/dim_product.yaml");

        // Create example table - FactSales
        var salesTable = new TableDefinition
        {
            Name = "FactSales",
            Description = "Sales fact table",
            MExpression = @"let
    Source = #table(
        {""SaleID"", ""ProductID"", ""SaleDate"", ""Quantity"", ""Amount""},
        {
            {1, 1, #date(2024, 1, 15), 2, 1999.98},
            {2, 2, #date(2024, 1, 16), 5, 149.95},
            {3, 3, #date(2024, 1, 17), 1, 299.99}
        }
    )
in
    Source",
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "SaleID", Type = "Int64", Description = "Unique sale identifier" },
                new() { Name = "ProductID", Type = "Int64", Description = "Product reference" },
                new() { Name = "SaleDate", Type = "DateTime", Description = "Date of sale" },
                new() { Name = "Quantity", Type = "Int64", Description = "Quantity sold" },
                new() { Name = "Amount", Type = "Decimal", FormatString = "$#,##0.00", Description = "Sale amount" }
            }
        };

        serializer.SaveToFile(salesTable, Path.Combine(projectPath, "tables", "fact_sales.yaml"));
        Console.WriteLine("  Created tables/fact_sales.yaml");

        // Create example model (includes project-level configuration)
        var model = new ModelDefinition
        {
            Name = "SalesModel",
            Description = "Example sales analytics model",
            CompatibilityLevel = 1600,
            Tables = new List<TableReference>
            {
                new() { Ref = "DimProduct" },
                new() { Ref = "FactSales" }
            },
            Relationships = new List<RelationshipDefinition>
            {
                new()
                {
                    FromTable = "FactSales",
                    FromColumn = "ProductID",
                    ToTable = "DimProduct",
                    ToColumn = "ProductID",
                    Cardinality = "ManyToOne",
                    CrossFilterDirection = "Single"
                }
            },
            Measures = new List<MeasureDefinition>
            {
                new()
                {
                    Name = "Total Sales",
                    Table = "FactSales",
                    Expression = "SUM(FactSales[Amount])",
                    FormatString = "$#,##0.00",
                    DisplayFolder = "Sales Metrics"
                },
                new()
                {
                    Name = "Total Quantity",
                    Table = "FactSales",
                    Expression = "SUM(FactSales[Quantity])",
                    FormatString = "#,##0",
                    DisplayFolder = "Sales Metrics"
                },
                new()
                {
                    Name = "Average Sale Amount",
                    Table = "FactSales",
                    Expression = "DIVIDE([Total Sales], COUNTROWS(FactSales))",
                    FormatString = "$#,##0.00",
                    DisplayFolder = "Sales Metrics"
                }
            }
        };

        serializer.SaveToFile(model, Path.Combine(projectPath, "models", "sales_model.yaml"));
        Console.WriteLine("  Created models/sales_model.yaml");
        Console.WriteLine();
    }
}
