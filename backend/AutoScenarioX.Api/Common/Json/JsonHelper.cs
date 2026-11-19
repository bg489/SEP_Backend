using System.Text.Json;

namespace AutoScenarioX.Api.Common.Json;

/// <summary>Tiện ích chuyển chuỗi jsonb (lưu dạng string) sang JsonElement / mảng chuỗi.</summary>
public static class JsonHelper
{
    /// <summary>Chuỗi JSON -> JsonElement để trả nguyên cấu trúc trong response.</summary>
    public static JsonElement? ToElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();   // Clone để dùng được sau khi doc bị dispose
    }

    /// <summary>Chuỗi JSON dạng mảng chuỗi (vd event_types) -> string[].</summary>
    public static string[] ToStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
        return doc.RootElement.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();
    }
}
