using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Common.Json;

/// <summary>Một khác biệt giữa hai tài liệu JSON. Path là JSON Pointer, vd "/route/speed_limit_kmh".</summary>
public sealed record JsonDiffEntry(string Path, string Change, JsonNode? Before, JsonNode? After);

public static class JsonDiffChanges
{
    public const string Added = "added";
    public const string Removed = "removed";
    public const string Changed = "changed";
}

/// <summary>
/// So sánh hai nội dung scenario để hiện "đã đổi gì giữa version N và M".
/// So ở mức lá: chỉ báo đúng trường thay đổi chứ không báo cả khối cha.
/// </summary>
public static class JsonDiff
{
    public static IReadOnlyList<JsonDiffEntry> Compare(JsonNode? before, JsonNode? after)
    {
        var entries = new List<JsonDiffEntry>();
        Walk(before, after, string.Empty, entries);
        return entries;
    }

    private static void Walk(JsonNode? before, JsonNode? after, string path, List<JsonDiffEntry> entries)
    {
        if (before is JsonObject beforeObject && after is JsonObject afterObject)
        {
            var keys = beforeObject.Select(p => p.Key)
                .Union(afterObject.Select(p => p.Key), StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal);

            foreach (var key in keys)
            {
                var childPath = $"{path}/{Escape(key)}";
                beforeObject.TryGetPropertyValue(key, out var beforeChild);
                afterObject.TryGetPropertyValue(key, out var afterChild);

                if (!beforeObject.ContainsKey(key)) entries.Add(Entry(childPath, JsonDiffChanges.Added, null, afterChild));
                else if (!afterObject.ContainsKey(key)) entries.Add(Entry(childPath, JsonDiffChanges.Removed, beforeChild, null));
                else Walk(beforeChild, afterChild, childPath, entries);
            }
            return;
        }

        if (before is JsonArray beforeArray && after is JsonArray afterArray)
        {
            for (var i = 0; i < Math.Max(beforeArray.Count, afterArray.Count); i++)
            {
                var childPath = $"{path}/{i}";
                if (i >= beforeArray.Count) entries.Add(Entry(childPath, JsonDiffChanges.Added, null, afterArray[i]));
                else if (i >= afterArray.Count) entries.Add(Entry(childPath, JsonDiffChanges.Removed, beforeArray[i], null));
                else Walk(beforeArray[i], afterArray[i], childPath, entries);
            }
            return;
        }

        if (!Equivalent(before, after))
            entries.Add(Entry(path.Length == 0 ? "/" : path, JsonDiffChanges.Changed, before, after));
    }

    private static JsonDiffEntry Entry(string path, string change, JsonNode? before, JsonNode? after)
        => new(path, change, before?.DeepClone(), after?.DeepClone());

    private static bool Equivalent(JsonNode? left, JsonNode? right)
        => CanonicalJsonSerializer.Serialize(left?.DeepClone()) == CanonicalJsonSerializer.Serialize(right?.DeepClone());

    /// <summary>Escape theo RFC 6901 (JSON Pointer).</summary>
    private static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");
}
