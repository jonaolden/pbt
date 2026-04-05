using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class IncrementalRefreshTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void ComposeModel_WithIncrementalRefresh_ShouldAddRangeExpressions()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"ir_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        var tablesPath = Path.Combine(tempPath, "tables");
        Directory.CreateDirectory(tablesPath);

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table({}, {}) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "OrderDate", Type = "DateTime", SourceColumn = "OrderDate" },
                    new() { Name = "Amount", Type = "Decimal", SourceColumn = "Amount" }
                },
                IncrementalRefresh = new IncrementalRefreshDefinition
                {
                    DateColumn = "OrderDate",
                    Granularity = "Day",
                    IncrementalPeriods = 30,
                    IncrementalPeriodOffset = 365
                }
            };

            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "sales.yaml"));

            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } }
            };

            var composer = new ModelComposer(registry);
            var database = composer.ComposeModel(modelDef);

            // Should have RangeStart and RangeEnd expressions
            Assert.NotNull(database.Model.Expressions.Find("RangeStart"));
            Assert.NotNull(database.Model.Expressions.Find("RangeEnd"));

            // Should have refresh policy annotation
            var salesTable = database.Model.Tables.Find("Sales");
            Assert.NotNull(salesTable);
            Assert.Contains(salesTable.Annotations, a => a.Name == "PBI_IncrementalRefresh");
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void ComposeModel_WithoutIncrementalRefresh_ShouldNotAddRangeExpressions()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"ir_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        var tablesPath = Path.Combine(tempPath, "tables");
        Directory.CreateDirectory(tablesPath);

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                MExpression = "let Source = #table({}, {}) in Source",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal", SourceColumn = "Amount" }
                }
            };

            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "sales.yaml"));

            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "Sales" } }
            };

            var composer = new ModelComposer(registry);
            var database = composer.ComposeModel(modelDef);

            Assert.Null(database.Model.Expressions.Find("RangeStart"));
            Assert.Null(database.Model.Expressions.Find("RangeEnd"));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void IncrementalRefreshDefinition_ShouldSerializeRoundTrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ir_yaml_{Guid.NewGuid()}.yaml");

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Date", Type = "DateTime", SourceColumn = "DATE" }
                },
                IncrementalRefresh = new IncrementalRefreshDefinition
                {
                    DateColumn = "Date",
                    Granularity = "Month",
                    IncrementalPeriods = 12,
                    IncrementalPeriodOffset = 24,
                    PollingExpression = "MAX(Sales[ModifiedDate])"
                }
            };

            _serializer.SaveToFile(tableDef, tempFile);
            var loaded = _serializer.LoadFromFile<TableDefinition>(tempFile);

            Assert.NotNull(loaded.IncrementalRefresh);
            Assert.Equal("Date", loaded.IncrementalRefresh.DateColumn);
            Assert.Equal("Month", loaded.IncrementalRefresh.Granularity);
            Assert.Equal(12, loaded.IncrementalRefresh.IncrementalPeriods);
            Assert.Equal(24, loaded.IncrementalRefresh.IncrementalPeriodOffset);
            Assert.Equal("MAX(Sales[ModifiedDate])", loaded.IncrementalRefresh.PollingExpression);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
