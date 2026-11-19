using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Services.Oracle;

/// <summary>Telemetry asx.telemetry/1 đã đọc (xem docs/telemetry-format.md).</summary>
public sealed record Telemetry(JsonObject Header, IReadOnlyList<JsonObject> Ticks, IReadOnlyList<JsonObject> Events, JsonObject End);

/// <summary>File Telemetry không chấm được; Message (tiếng Anh) thành ErrorReason của Verdict ERROR.</summary>
public sealed class TelemetryFormatException(string message) : Exception(message);

/// <summary>
/// Đọc file JSON Lines asx.telemetry/1: mỗi dòng một bản ghi, phân loại theo "kind".
/// Kiểm kiểu mọi trường Oracle đọc ngay tại đây, để Check phía sau không phải đề phòng dữ liệu hỏng.
/// </summary>
public static class TelemetryReader
{
    public const string Format = "asx.telemetry/1";

    public static Telemetry Read(string text)
    {
        JsonObject? header = null, end = null;
        var ticks = new List<JsonObject>();
        var events = new List<JsonObject>();

        var lines = text.Split('\n');
        var lastLine = Array.FindLastIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var lineNo = i + 1;
            JsonNode? node;
            try { node = JsonNode.Parse(lines[i]); }
            catch (JsonException)
            {
                // Dòng cuối hỏng = runner chết giữa chừng -> báo "truncated" bên dưới. Dòng giữa hỏng = file hỏng.
                if (i == lastLine) break;
                throw new TelemetryFormatException($"Telemetry line {lineNo} is not valid JSON.");
            }
            if (node is not JsonObject record)
                throw new TelemetryFormatException($"Telemetry line {lineNo} is not a JSON object.");

            switch (RequireString(record, "kind", lineNo))
            {
                case "header":
                    RequireString(record, "format", lineNo);
                    RequireHeaderFields(record, lineNo);
                    header = record;
                    break;
                case "tick":
                    RequireTimeAndPostStop(record, lineNo);
                    RequireTickFields(record, lineNo);
                    ticks.Add(record);
                    break;
                case "event":
                    RequireTimeAndPostStop(record, lineNo);
                    var type = RequireString(record, "type", lineNo);
                    if (type == "lane_invasion") RequireStringArray(record, "markings", lineNo);
                    events.Add(record);
                    break;
                case "end":
                    RequireString(record, "outcome", lineNo);
                    if (record["reason"] is not null) RequireString(record, "reason", lineNo);
                    end = record;
                    break;
            }
        }

        if (header is null)
            throw new TelemetryFormatException("Telemetry has no header record.");
        var format = (string)header["format"]!;
        if (format != Format)
            throw new TelemetryFormatException($"Unsupported telemetry format '{format}'.");
        if (end is null)
            throw new TelemetryFormatException("Telemetry is truncated: no end-of-run record.");

        return new Telemetry(header, ticks, events, end);
    }

    private static string RequireString(JsonObject record, string key, int lineNo) =>
        record[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String
            ? v.GetValue<string>()
            : throw new TelemetryFormatException($"Telemetry line {lineNo}: '{key}' must be a string.");

    private static void RequireHeaderFields(JsonObject header, int lineNo)
    {
        RequireOptionalNumber(header, "warmup_s", lineNo);
        RequireOptionalNumber(header, "route_speed_limit_kmh", lineNo);
        RequireOptionalNumbers(header, "ego_extent", ["x", "y"], lineNo);
        RequireOptionalNumbers(header, "goal", ["x", "y"], lineNo);
    }

    private static void RequireTickFields(JsonObject tick, int lineNo)
    {
        RequireNumbers(tick, "ego", ["x", "y", "vx", "vy"], lineNo);
        RequireOptionalNumber((JsonObject)tick["ego"]!, "speed_limit_kmh", lineNo);
        if (tick["actors"] is null) return;
        if (tick["actors"] is not JsonArray actors)
            throw new TelemetryFormatException($"Telemetry line {lineNo}: 'actors' must be an array.");
        foreach (var a in actors)
        {
            if (a is not JsonObject actor)
                throw new TelemetryFormatException($"Telemetry line {lineNo}: 'actors' must contain objects.");
            RequireNumbers(actor, "extent", ["x", "y"], lineNo);
            foreach (var key in new[] { "x", "y", "vx", "vy" }) RequireOptionalNumber(actor, key, lineNo, required: true);
        }
    }

    private static void RequireNumbers(JsonObject parent, string key, string[] fields, int lineNo)
    {
        if (parent[key] is not JsonObject obj)
            throw new TelemetryFormatException($"Telemetry line {lineNo}: '{key}' must be an object.");
        foreach (var f in fields) RequireOptionalNumber(obj, f, lineNo, required: true, path: $"{key}.{f}");
    }

    private static void RequireOptionalNumbers(JsonObject parent, string key, string[] fields, int lineNo)
    {
        if (parent[key] is not null) RequireNumbers(parent, key, fields, lineNo);
    }

    private static void RequireOptionalNumber(JsonObject obj, string key, int lineNo, bool required = false, string? path = null)
    {
        if (obj[key] is null && !required) return;
        if (obj[key] is not JsonValue v || v.GetValueKind() != JsonValueKind.Number)
            throw new TelemetryFormatException($"Telemetry line {lineNo}: '{path ?? key}' must be a number.");
    }

    private static void RequireStringArray(JsonObject record, string key, int lineNo)
    {
        if (record[key] is not JsonArray a || a.Any(x => x is not JsonValue v || v.GetValueKind() != JsonValueKind.String))
            throw new TelemetryFormatException($"Telemetry line {lineNo}: '{key}' must be an array of strings.");
    }

    private static void RequireTimeAndPostStop(JsonObject record, int lineNo)
    {
        if (record["t"] is not JsonValue t || t.GetValueKind() != JsonValueKind.Number)
            throw new TelemetryFormatException($"Telemetry line {lineNo}: 't' must be a number.");
        if (record["post_stop"] is not JsonValue p || p.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
            throw new TelemetryFormatException($"Telemetry line {lineNo}: 'post_stop' must be true or false.");
    }
}
