using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class TableGeneratorTests : IDisposable
{
    private readonly YamlSerializer _yaml = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gen_test_{Guid.NewGuid()}");

    public TableGeneratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeAdapter : ISourceAdapter
    {
        public List<ColumnDefinition> Columns = new()
        {
            new() { Name = "PROPERTY_ID", Type = "Int64", SourceColumn = "PROPERTY_ID" },
            new() { Name = "NAME", Type = "String", SourceColumn = "NAME" },
            new() { Name = "INTERNAL_CODE", Type = "String", SourceColumn = "INTERNAL_CODE" },
            new() { Name = "CREATED_AT", Type = "DateTime", SourceColumn = "CREATED_AT" }
        };
        public string? ValidateTarget(string target) => new SnowflakeAdapter().ValidateTarget(target);
        public TableDefinition GetTableMetadata(string target) =>
            target.Contains("MISSING") ? throw new InvalidOperationException("table not found")
            : new() { Description = "src comment", Columns = Columns.Select(c => new ColumnDefinition { Name = c.Name, Type = c.Type, SourceColumn = c.SourceColumn }).ToList() };
        public SourceDefinition ToSource(string target, SourceConnectionConfig c) =>
            new() { Type = "snowflake", Connector = c.Connector, Database = "DB", Schema = "SCH", Table = "TBL" };
    }

    private static ProjectConfig Config(params TableGenSpec[] tables) => new()
    {
        Sources = { ["snowflake"] = new() { Connector = "SnowflakeSource" } },
        Tables = tables.ToList()
    };

    private static TableGenSpec Spec(string name = "Property", string target = "db.sch.tbl") =>
        new() { TableName = name, Source = "snowflake", Target = target };

    private TableGenerator Gen() => new(new(StringComparer.OrdinalIgnoreCase) { ["snowflake"] = new FakeAdapter() });

    private string TablePath(string name = "property") => Path.Combine(_dir, "tables", name + ".yaml");

    [Theory]
    [InlineData("db.sch.tbl", "DB", "SCH", "TBL")]
    [InlineData("\"MyDb\".sch.\"Mixed Case\"", "MyDb", "SCH", "Mixed Case")]
    [InlineData("Db.\"a\"\"b\".t$1", "DB", "a\"b", "T$1")]
    public void SnowflakeTarget_Parse_FoldsAndQuotes(string target, string db, string schema, string table) =>
        Assert.Equal((db, schema, table), SnowflakeTarget.Parse(target));

    [Theory]
    [InlineData("sch.tbl")]
    [InlineData("a.b.c.d")]
    [InlineData("a..c")]
    [InlineData("a.b.\"c")]
    [InlineData("a.b.has space")]
    [InlineData("")]
    public void SnowflakeTarget_Parse_Rejects(string target) =>
        Assert.Throws<ArgumentException>(() => SnowflakeTarget.Parse(target));

    [Fact]
    public void Validate_ReportsActionableErrors()
    {
        var cfg = Config(
            Spec("A"), Spec("a"), Spec(""),
            new() { TableName = "B", Source = "oracle", Target = "x.y.z" },
            Spec("C", "only.two"),
            new() { TableName = "D", Source = "snowflake", Target = "" });
        var errors = Gen().Validate(cfg);
        Assert.Null(errors[0]);
        Assert.Contains("duplicate", errors[1]);
        Assert.Contains("table_name is required", errors[2]);
        Assert.Contains("not configured", errors[3]);
        Assert.Contains("expected database.schema.table", errors[4]);
        Assert.Contains("target is required", errors[5]);
    }

    [Fact]
    public void Generate_CreatesThenIsIdempotent()
    {
        var cfg = Config(Spec());
        var r1 = Gen().Generate(cfg, _dir, false);
        Assert.Equal("created", r1.Tables[0].Status);
        var first = File.ReadAllText(TablePath());
        var stamp = File.GetLastWriteTimeUtc(TablePath());

        var r2 = Gen().Generate(cfg, _dir, false);
        Assert.Equal("unchanged", r2.Tables[0].Status);
        Assert.Equal(first, File.ReadAllText(TablePath()));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(TablePath()));
    }

    [Fact]
    public void Generate_DryRun_WritesNothing()
    {
        var r = Gen().Generate(Config(Spec()), _dir, true);
        Assert.Equal("created", r.Tables[0].Status);
        Assert.False(File.Exists(TablePath()));
    }

    [Fact]
    public void Generate_MissingMetadata_ReportsErrorAndContinues()
    {
        var r = Gen().Generate(Config(Spec("Bad", "db.sch.MISSING"), Spec("Good")), _dir, false);
        Assert.Equal("error", r.Tables[0].Status);
        Assert.Contains("table not found", r.Tables[0].Error);
        Assert.Equal("created", r.Tables[1].Status);
        Assert.False(File.Exists(TablePath("bad")));
    }

    [Fact]
    public void Generate_AppliesOverrides_ColumnOrderKept()
    {
        var spec = Spec();
        spec.Options = new() { Hidden = true, Mode = "import" };
        spec.ColumnOverrides = new()
        {
            ["property_id"] = new() { Name = "PropertyId", DataType = "Int64", IsKey = true },
            ["INTERNAL_CODE"] = new() { IsHidden = true },
            ["CREATED_AT"] = new() { FormatString = "yyyy-mm-dd" }
        };
        Gen().Generate(Config(spec), _dir, false);
        var t = _yaml.LoadFromFile<TableDefinition>(TablePath());

        Assert.True(t.IsHidden);
        Assert.Equal(new[] { "PropertyId", "NAME", "INTERNAL_CODE", "CREATED_AT" }, t.Columns.Select(c => c.Name));
        Assert.True(t.Columns[0].IsKey);
        Assert.True(t.Columns[2].IsHidden);
        Assert.Equal("yyyy-mm-dd", t.Columns[3].FormatString);
        Assert.Equal("SnowflakeSource", t.Source!.Connector);
    }

    [Fact]
    public void Generate_UnknownOverrideColumn_Errors()
    {
        var spec = Spec();
        spec.ColumnOverrides = new() { ["NOPE"] = new() { IsHidden = true } };
        var r = Gen().Generate(Config(spec), _dir, false);
        Assert.Equal("error", r.Tables[0].Status);
        Assert.False(File.Exists(TablePath()));
    }

    [Fact]
    public void Generate_PreservesManualContent()
    {
        var cfg = Config(Spec());
        Gen().Generate(cfg, _dir, false);

        var t = _yaml.LoadFromFile<TableDefinition>(TablePath());
        t.Description = "manual description";
        t.Measures.Add(new() { Name = "Count", Table = "Property", Expression = "COUNTROWS(Property)" });
        t.Hierarchies.Add(new() { Name = "H", Levels = new() { new() { Name = "L", Column = "NAME" } } });
        t.DataCategory = "Time";
        t.Columns[1].Description = "manual col description";
        _yaml.SaveToFile(t, TablePath());

        Assert.Equal("unchanged", Gen().Generate(cfg, _dir, false).Tables[0].Status);
        var after = _yaml.LoadFromFile<TableDefinition>(TablePath());
        Assert.Equal("manual description", after.Description);
        Assert.Single(after.Measures);
        Assert.Single(after.Hierarchies);
        Assert.Equal("Time", after.DataCategory);
        Assert.Equal("manual col description", after.Columns[1].Description);
    }

    [Fact]
    public void Generate_UnreadableExistingFile_IsNotOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TablePath())!);
        File.WriteAllText(TablePath(), "name: [unterminated");
        var r = Gen().Generate(Config(Spec()), _dir, false);
        Assert.Equal("error", r.Tables[0].Status);
        Assert.Equal("name: [unterminated", File.ReadAllText(TablePath()));
    }

    [Fact]
    public void Generate_AddsModelRef_Once()
    {
        File.WriteAllText(Path.Combine(_dir, "m.yaml"), "name: M\ntables:\n  - ref: Other\n");
        var cfg = Config(Spec());
        cfg.Model = "m.yaml";

        Assert.Equal(new[] { "Property" }, Gen().Generate(cfg, _dir, false).RefsAdded);
        Assert.Empty(Gen().Generate(cfg, _dir, false).RefsAdded);
        var m = _yaml.LoadFromFile<ModelDefinition>(Path.Combine(_dir, "m.yaml"));
        Assert.Equal(new[] { "Other", "Property" }, m.Tables.Select(r => r.Ref));
    }

    [Fact]
    public void Generate_OutputComposesAndParsesAsTmdl()
    {
        Gen().Generate(Config(Spec()), _dir, false);
        var registry = new TableRegistry(_yaml);
        registry.LoadTables(Path.Combine(_dir, "tables"));
        var model = new ModelDefinition { Name = "M", Tables = { new() { Ref = "Property" } } };
        var db = new ModelComposer(registry).ComposeModel(model, projectRootPath: _dir);

        var tmdl = Path.Combine(_dir, "out");
        TmdlSerializer.SerializeDatabaseToFolder(db, tmdl);
        var back = TmdlSerializer.DeserializeDatabaseFromFolder(tmdl);

        var table = back.Model.Tables["Property"];
        Assert.Equal(4, table.Columns.Count);
        Assert.Single(table.Partitions);
    }

    [Fact]
    public void SnowflakeAdapter_ImportsPluginTmdl_WithStubbedPlugin()
    {
        string? calledWith = null;
        var adapter = new SnowflakeAdapter((refText, outFile) =>
        {
            calledWith = refText;
            File.WriteAllText(outFile,
                "table TBL\n\tcolumn ID\n\t\tdataType: int64\n\t\tsourceColumn: ID\n\n\tcolumn NAME\n\t\tdataType: string\n\t\tsourceColumn: NAME\n");
            return (0, "");
        });

        var meta = adapter.GetTableMetadata("db.\"Sch\".tbl");

        Assert.Equal("DB.Sch.TBL", calledWith);
        Assert.Equal(new[] { "ID", "NAME" }, meta.Columns.Select(c => c.Name));
        Assert.Equal("int64", meta.Columns[0].Type.ToLowerInvariant());
        Assert.Throws<InvalidOperationException>(() =>
            new SnowflakeAdapter((_, _) => (1, "boom")).GetTableMetadata("a.b.c"));
    }
}
