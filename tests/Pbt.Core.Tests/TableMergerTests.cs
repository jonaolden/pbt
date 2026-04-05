using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class TableMergerTests : IDisposable
{
    private readonly YamlSerializer _serializer = new();
    private readonly string _tempDir;

    public TableMergerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"merger_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void MergeTable_NoExistingFile_ShouldReturnGenerated()
    {
        var merger = new TableMerger(new MergeOptions());
        var generated = CreateTable("Sales", ("ID", "Int64", "ID"), ("Amount", "Decimal", "AMOUNT"));
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        var result = merger.MergeTable(generated, filePath);

        Assert.Equal("Sales", result.Name);
        Assert.Equal(2, result.Columns.Count);
    }

    [Fact]
    public void MergeTable_ExistingFile_ShouldPreserveManualDescription()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        // Create existing with manual description
        var existing = CreateTable("Sales", ("ID", "Int64", "ID"));
        existing.Columns[0].Description = "Manually written description";
        _serializer.SaveToFile(existing, filePath);

        // Generate new version without description
        var generated = CreateTable("Sales", ("ID", "Int64", "ID"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Equal("Manually written description", result.Columns[0].Description);
    }

    [Fact]
    public void MergeTable_ExistingFile_ShouldPreserveHierarchies()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true });
        var filePath = Path.Combine(_tempDir, "geo.yaml");

        var existing = CreateTable("Geo", ("Country", "String", "COUNTRY"), ("City", "String", "CITY"));
        existing.Hierarchies = new List<HierarchyDefinition>
        {
            new() { Name = "Geography", Levels = new List<LevelDefinition>
            {
                new() { Name = "Country", Column = "Country" },
                new() { Name = "City", Column = "City" }
            }}
        };
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("Geo", ("Country", "String", "COUNTRY"), ("City", "String", "CITY"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Single(result.Hierarchies);
        Assert.Equal("Geography", result.Hierarchies[0].Name);
    }

    [Fact]
    public void MergeTable_ExistingFile_ShouldPreserveMeasures()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        var existing = CreateTable("Sales", ("Amount", "Decimal", "AMOUNT"));
        existing.Measures = new List<MeasureDefinition>
        {
            new() { Name = "Total Sales", Expression = "SUM(Sales[Amount])" }
        };
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("Sales", ("Amount", "Decimal", "AMOUNT"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Single(result.Measures);
        Assert.Equal("Total Sales", result.Measures[0].Name);
    }

    [Fact]
    public void MergeTable_NewColumn_ShouldBeAdded()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        var existing = CreateTable("Sales", ("ID", "Int64", "ID"));
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("Sales", ("ID", "Int64", "ID"), ("Amount", "Decimal", "AMOUNT"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Equal(2, result.Columns.Count);
        Assert.Contains(result.Columns, c => c.Name == "Amount");
    }

    [Fact]
    public void MergeTable_DeletedColumn_ShouldBePreservedByDefault()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true, PruneDeleted = false });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        var existing = CreateTable("Sales", ("ID", "Int64", "ID"), ("OldCol", "String", "OLD_COL"));
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("Sales", ("ID", "Int64", "ID"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Equal(2, result.Columns.Count);
        Assert.Contains(result.Columns, c => c.Name == "OldCol");
    }

    [Fact]
    public void MergeTable_DeletedColumn_ShouldBePrunedWhenConfigured()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true, PruneDeleted = true });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        var existing = CreateTable("Sales", ("ID", "Int64", "ID"), ("OldCol", "String", "OLD_COL"));
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("Sales", ("ID", "Int64", "ID"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Single(result.Columns);
        Assert.DoesNotContain(result.Columns, c => c.Name == "OldCol");
    }

    [Fact]
    public void MergeTable_TypeChange_ShouldUpdateWhenConfigured()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true, UpdateTypes = true });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        var existing = CreateTable("Sales", ("Amount", "String", "AMOUNT"));
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("Sales", ("Amount", "Decimal", "AMOUNT"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Equal("Decimal", result.Columns[0].Type);
    }

    [Fact]
    public void MergeTable_TypeChange_ShouldPreserveWhenNotConfigured()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true, UpdateTypes = false });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        var existing = CreateTable("Sales", ("Amount", "String", "AMOUNT"));
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("Sales", ("Amount", "Decimal", "AMOUNT"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Equal("String", result.Columns[0].Type);
    }

    [Fact]
    public void MergeTable_MatchBySourceColumn_ShouldHandleRenamedColumns()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true });
        var filePath = Path.Combine(_tempDir, "sales.yaml");

        // Existing has column renamed from "CustId" but same SourceColumn
        var existing = CreateTable("Sales");
        existing.Columns.Add(new ColumnDefinition
        {
            Name = "CustomerId",
            Type = "Int64",
            SourceColumn = "CUST_ID",
            Description = "FK to customers"
        });
        _serializer.SaveToFile(existing, filePath);

        // Generated has new name for same source column
        var generated = CreateTable("Sales");
        generated.Columns.Add(new ColumnDefinition
        {
            Name = "CustId",
            Type = "Int64",
            SourceColumn = "CUST_ID"
        });

        var result = merger.MergeTable(generated, filePath);

        Assert.Single(result.Columns);
        Assert.Equal("CustId", result.Columns[0].Name); // Takes generated name
        Assert.Equal("FK to customers", result.Columns[0].Description); // Preserves manual description
    }

    [Fact]
    public void MergeTable_ShouldPreserveFormatStringAndSortBy()
    {
        var merger = new TableMerger(new MergeOptions { DryRun = true });
        var filePath = Path.Combine(_tempDir, "date.yaml");

        var existing = CreateTable("DateDim", ("MonthName", "String", "MONTH_NAME"));
        existing.Columns[0].FormatString = "MMM";
        existing.Columns[0].SortByColumn = "MonthNum";
        _serializer.SaveToFile(existing, filePath);

        var generated = CreateTable("DateDim", ("MonthName", "String", "MONTH_NAME"));

        var result = merger.MergeTable(generated, filePath);

        Assert.Equal("MMM", result.Columns[0].FormatString);
        Assert.Equal("MonthNum", result.Columns[0].SortByColumn);
    }

    private static TableDefinition CreateTable(string name, params (string Name, string Type, string SourceColumn)[] columns)
    {
        return new TableDefinition
        {
            Name = name,
            Columns = columns.Select(c => new ColumnDefinition
            {
                Name = c.Name,
                Type = c.Type,
                SourceColumn = c.SourceColumn
            }).ToList()
        };
    }
}
