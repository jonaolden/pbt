using System.Text;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// CSV adapter: reads an INFORMATION_SCHEMA.COLUMNS-style export. Target is <c>path/to/schema.csv#TABLE_NAME</c>
/// (path relative to the working directory). Required headers (case-insensitive): table_name, column_name, data_type.
/// Optional: ordinal_position, table_comment, column_comment, table_catalog, table_schema.
/// </summary>
public sealed class CsvAdapter : ISourceAdapter
{
    private static readonly string[] Required = { "table_name", "column_name", "data_type" };

    public string? ValidateTarget(string target)
    {
        var (path, table) = Split(target);
        return string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(table)
            ? $"CSV target '{target}' must look like path/to/schema.csv#TABLE_NAME."
            : null;
    }

    public TableDefinition GetTableMetadata(string target)
    {
        var rows = Rows(target);
        var cols = rows.Select(r => (Row: r, Type: SourceTypes.ToTmdl(r["data_type"]))).Select(x => new ColumnDefinition
        {
            Name = x.Row["column_name"],
            Type = x.Type,
            SourceColumn = x.Row["column_name"],
            Description = Opt(x.Row, "column_comment"),
            SummarizeBy = SourceTypes.SummarizeBy(x.Type)
        }).ToList();
        return new TableDefinition { Name = Split(target).Table, Description = Opt(rows[0], "table_comment"), Columns = cols };
    }

    public SourceDefinition ToSource(string target, SourceConnectionConfig connection)
    {
        var first = Rows(target)[0];
        return new SourceDefinition
        {
            Type = connection.Type ?? "snowflake",
            Connector = connection.Connector,
            Connection = connection.Connection,
            Database = Opt(first, "table_catalog"),
            Schema = Opt(first, "table_schema"),
            Table = first["table_name"]
        };
    }

    private static (string Path, string Table) Split(string target)
    {
        var i = target.LastIndexOf('#');
        return i < 0 ? (target, "") : (target[..i].Trim(), target[(i + 1)..].Trim());
    }

    private static string? Opt(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    /// <summary>Rows of one table, ordered by ordinal_position (stable; missing positions last).</summary>
    private static List<Dictionary<string, string>> Rows(string target)
    {
        var (path, table) = Split(target);
        if (!File.Exists(path)) throw new InvalidOperationException($"CSV file not found: {path}");

        var records = Parse(File.ReadAllText(path, Encoding.UTF8));
        if (records.Count == 0) throw new InvalidOperationException($"No records found in CSV file: {path}");
        var header = records[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var missing = Required.Where(c => !header.Contains(c)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"CSV is missing required column(s): {string.Join(", ", missing)}");

        var rows = records.Skip(1).Where(r => r.Any(f => f.Trim().Length > 0))
            .Select(r => header.Select((h, i) => (h, v: i < r.Count ? r[i].Trim() : "")).Where(p => p.h.Length > 0)
                .ToDictionary(p => p.h, p => p.v))
            .ToList();
        if (rows.Any(r => Required.Any(c => r[c].Length == 0)))
            throw new InvalidOperationException($"CSV row is missing table_name/column_name/data_type in {path}.");

        var mine = rows.Where(r => r["table_name"].Equals(table, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => int.TryParse(Opt(r, "ordinal_position"), out var n) ? n : int.MaxValue)
            .ToList();
        if (mine.Count == 0) throw new InvalidOperationException($"Table '{table}' not found in {path}.");
        return mine;
    }

    /// <summary>Minimal RFC 4180 parser: quoted fields, "" escapes, embedded newlines. Strips a leading BOM.</summary>
    internal static List<List<string>> Parse(string text)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        text = text.TrimStart('﻿');

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                records.Add(row); row = new();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); records.Add(row); }
        return records;
    }
}
