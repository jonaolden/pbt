using System.Text.Json.Nodes;
using Microsoft.AnalysisServices.Tabular;

namespace Pbt.Core.Services;

public record TomChange(string ChangeType, string ObjectPath, string? OldValue, string? NewValue, bool IsBreaking);

/// <summary>
/// Property-level diff of two composed TOM databases. Works on the TOM JSON form, so every
/// property TOM knows about is compared, not only the ones the YAML schema models.
/// </summary>
public static class TomDiff
{
    // Properties that change without the model changing, or that the server owns
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "lineageTag", "sourceLineageTag", "modifiedTime", "structureModifiedTime", "refreshedTime", "lastUpdate", "lastSchemaUpdate", "lastProcessed"
    };

    // Changing these can break reports or refreshes
    private static readonly HashSet<string> BreakingProperties = new(StringComparer.Ordinal)
    {
        "dataType", "sourceColumn", "fromCardinality", "toCardinality", "isActive", "crossFilteringBehavior"
    };

    public static List<TomChange> Diff(Database a, Database b)
    {
        var options = new SerializeOptions { IgnoreInferredObjects = true, IgnoreInferredProperties = true, IgnoreTimestamps = true };
        var changes = new List<TomChange>();
        Compare(Parse(a, options), Parse(b, options), a.Name, changes);
        return changes;
    }

    private static JsonNode? Parse(Database db, SerializeOptions options) =>
        JsonNode.Parse(JsonSerializer.SerializeDatabase(db, options))?["model"];

    private static void Compare(JsonNode? a, JsonNode? b, string path, List<TomChange> changes)
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
            {
                if (Ignored.Contains(key)) continue;
                var childPath = $"{path}.{key}";
                var hasA = oa.TryGetPropertyValue(key, out var va);
                var hasB = ob.TryGetPropertyValue(key, out var vb);
                if (hasA && hasB) Compare(va, vb, childPath, changes);
                else if (va is JsonObject or JsonArray || vb is JsonObject or JsonArray)
                    changes.Add(new TomChange(hasA ? "property_removed" : "property_added", childPath, null, null, false));
                else
                    changes.Add(new TomChange("property_changed", childPath, va?.ToJsonString(), vb?.ToJsonString(), BreakingProperties.Contains(key)));
            }
        }
        else if (a is JsonArray aa && b is JsonArray ab)
        {
            if (aa.All(IsNamed) && ab.All(IsNamed))
                CompareNamed(aa, ab, path, changes);
            else if (aa.ToJsonString() != ab.ToJsonString())
                changes.Add(new TomChange("property_changed", path, aa.ToJsonString(), ab.ToJsonString(), false));
        }
        else if (a?.ToJsonString() != b?.ToJsonString())
        {
            var key = path[(path.LastIndexOf('.') + 1)..];
            changes.Add(new TomChange("property_changed", path, a?.ToJsonString(), b?.ToJsonString(), BreakingProperties.Contains(key)));
        }
    }

    private static bool IsNamed(JsonNode? n) => n is JsonObject o && o["name"] is JsonValue;

    private static void CompareNamed(JsonArray a, JsonArray b, string path, List<TomChange> changes)
    {
        var da = a.ToDictionary(n => n!["name"]!.GetValue<string>(), StringComparer.Ordinal);
        var db = b.ToDictionary(n => n!["name"]!.GetValue<string>(), StringComparer.Ordinal);

        foreach (var name in da.Keys.Except(db.Keys))
            changes.Add(new TomChange("object_removed", $"{path}[{name}]", null, null, true));
        foreach (var name in db.Keys.Except(da.Keys))
            changes.Add(new TomChange("object_added", $"{path}[{name}]", null, null, false));
        foreach (var name in da.Keys.Intersect(db.Keys))
            Compare(da[name], db[name], $"{path}[{name}]", changes);
    }
}
