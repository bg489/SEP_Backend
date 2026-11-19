using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Json;

namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>Tiện ích đọc nội dung scenario dùng chung cho các luật tầng 2 và 3.</summary>
public static class ScenarioContentReader
{
    public const string ParamRefKey = "$param";

    public static JsonObject Parameters(JsonObject root)
        => root["parameters"] as JsonObject ?? new JsonObject();

    public static JsonArray Actors(JsonObject root)
        => root["actors"] as JsonArray ?? new JsonArray();

    public static JsonArray Events(JsonObject root)
        => root["events"] as JsonArray ?? new JsonArray();

    /// <summary>Map id -> type của các actor khai báo tường minh.</summary>
    /// <summary>Vị trí của event có id; -1 nếu không có (trùng id thì lấy event đầu tiên).</summary>
    public static int IndexOfEvent(JsonArray events, string id)
    {
        for (var i = 0; i < events.Count; i++)
            if (events[i] is JsonObject evt && AsString(evt["id"]) == id) return i;
        return -1;
    }

    /// <summary>
    /// Giây mô phỏng mà trigger của event bắt đầu, nếu tính được: simulation_time trực tiếp, after_event cộng dồn
    /// delay_s theo chuỗi tới gốc simulation_time. Null khi gốc là trigger theo vị trí/TTC, thiếu event hoặc có vòng.
    /// </summary>
    public static double? FireTime(JsonArray events, int index, JsonObject parameters)
    {
        var total = 0.0;
        var seen = new HashSet<int>();
        for (var i = index; i >= 0 && seen.Add(i);)
        {
            if (events[i] is not JsonObject evt || evt["trigger"] is not JsonObject trigger) return null;
            switch (AsString(trigger["type"]))
            {
                case "simulation_time":
                    return TryGetNumber(trigger["time_s"], parameters, out var time) ? total + time : null;
                case "after_event":
                    if (!TryGetNumber(trigger["delay_s"], parameters, out var delay) ||
                        AsString(trigger["event_ref"]) is not { } next) return null;
                    total += delay;
                    i = IndexOfEvent(events, next);
                    break;
                default:
                    return null;
            }
        }
        return null;
    }

    public static Dictionary<string, string> ActorTypesById(JsonObject root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in Actors(root))
        {
            if (node is not JsonObject actor) continue;
            if (AsString(actor["id"]) is not { } id) continue;
            result[id] = AsString(actor["type"]) ?? string.Empty;
        }
        return result;
    }

    /// <summary>Tên tham số nếu node là tham chiếu {"$param": "..."}; ngược lại null.</summary>
    public static string? AsParamRef(JsonNode? node)
        => node is JsonObject obj && obj.Count == 1 && obj.ContainsKey(ParamRefKey)
            ? AsString(obj[ParamRefKey])
            : null;

    /// <summary>
    /// Đọc một trường số, tự giải tham chiếu {"$param": "..."} bằng giá trị hiện tại của tham số.
    /// Trả false khi thiếu, sai kiểu, hoặc tham số chưa khai báo.
    /// </summary>
    public static bool TryGetNumber(JsonNode? node, JsonObject parameters, out double value)
    {
        value = 0;

        if (AsParamRef(node) is { } paramName)
        {
            if (parameters[paramName] is not JsonObject definition) return false;
            return TryGetNumber(definition["value"], new JsonObject(), out value);
        }

        if (node is not JsonValue jsonValue) return false;

        if (jsonValue.TryGetValue<double>(out var asDouble)) { value = asDouble; return true; }
        if (jsonValue.TryGetValue<long>(out var asLong)) { value = asLong; return true; }
        if (jsonValue.TryGetValue<int>(out var asInt)) { value = asInt; return true; }
        if (jsonValue.TryGetValue<decimal>(out var asDecimal)) { value = (double)asDecimal; return true; }
        return false;
    }

    /// <summary>
    /// Thay tại chỗ mọi {"$param": "ten"} bằng bản sao giá trị hiện tại của tham số (parameters.ten.value).
    /// Chỉ gọi trên nội dung đã qua tầng 2 (ParamReferenceRule bảo đảm tham số tồn tại và có value).
    /// </summary>
    public static void ResolveParamReferences(JsonNode node, JsonObject parameters)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj.ToList())
            {
                if (AsParamRef(value) is { } name)
                    obj[key] = parameters[name]!["value"]!.DeepClone();
                else if (value is not null) ResolveParamReferences(value, parameters);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (AsParamRef(array[i]) is { } name)
                    array[i] = parameters[name]!["value"]!.DeepClone();
                else if (array[i] is { } value) ResolveParamReferences(value, parameters);
            }
        }
    }

    public static string? AsString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>Duyệt toàn bộ cây, trả về (JSON Pointer, node) của từng object.</summary>
    public static IEnumerable<(string Path, JsonObject Node)> WalkObjects(JsonNode? node, string path = "")
    {
        switch (node)
        {
            case JsonObject obj:
                yield return (path.Length == 0 ? "/" : path, obj);
                foreach (var property in obj)
                    foreach (var found in WalkObjects(property.Value, $"{path}/{Escape(property.Key)}"))
                        yield return found;
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    foreach (var found in WalkObjects(array[i], $"{path}/{i}"))
                        yield return found;
                break;
        }
    }

    /// <summary>So sánh hai node theo nội dung (dùng dạng canonical để không phụ thuộc thứ tự key).</summary>
    public static bool AreEquivalent(JsonNode? left, JsonNode? right)
        => CanonicalJsonSerializer.Serialize(left?.DeepClone()) == CanonicalJsonSerializer.Serialize(right?.DeepClone());

    /// <summary>Escape theo RFC 6901 (JSON Pointer).</summary>
    private static string Escape(string token)
        => token.Replace("~", "~0").Replace("/", "~1");
}
