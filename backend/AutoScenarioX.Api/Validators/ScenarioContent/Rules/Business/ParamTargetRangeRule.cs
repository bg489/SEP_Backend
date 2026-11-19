using System.Globalization;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>
/// Tham số gắn vào một trường bằng {"$param": "ten"} phải nằm trong miền mà schema cho phép ở trường đó.
/// Tầng 1 không bắt được vì {"$param"} là nhánh hợp lệ; ParameterRangeRule chỉ so với [min, max] của chính tham số.
/// Kiểm tra value (giá trị đang dùng), min/max (miền sinh biến thể) và kiểu integer.
/// Miền đọc thẳng từ schema (nhánh số trong oneOf), nên trường mới có $param tự được kiểm tra.
/// </summary>
public sealed class ParamTargetRangeRule(IReferenceDataService referenceData) : IScenarioValidationRule
{
    private const string ParamRefPointer = "#/$defs/paramRef";

    private static JsonObject? _schema;

    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var schema = _schema ??= JsonNode.Parse(referenceData.GetScenarioSchemaJson())!.AsObject();
        var defs = schema["$defs"] as JsonObject ?? new JsonObject();
        var parameters = ScenarioContentReader.Parameters(context.Normalized);
        var issues = new List<ValidationIssue>();

        Check(schema, context.Normalized, "", defs, parameters, issues);
        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    /// <summary>Duyệt nội dung song song với schema để biết mỗi {"$param"} đang đứng ở trường nào.</summary>
    private void Check(JsonObject? schema, JsonNode? content, string path, JsonObject defs,
        JsonObject parameters, List<ValidationIssue> issues)
    {
        schema = Resolve(schema, defs);
        if (schema is null || content is null) return;

        if (schema["oneOf"] is JsonArray oneOf)
        {
            var branches = oneOf.OfType<JsonObject>().ToList();
            if (branches.Any(IsParamRefBranch))
            {
                if (ScenarioContentReader.AsParamRef(content) is { } name &&
                    branches.Where(b => !IsParamRefBranch(b)).Select(b => Resolve(b, defs)).FirstOrDefault() is { } numeric)
                    CheckReference(name, numeric, path, parameters, issues);
                return;
            }

            if (content is JsonObject chosenFor && Choose(branches.Select(b => Resolve(b, defs)), chosenFor) is { } chosen)
                Check(chosen, content, path, defs, parameters, issues);
            return;
        }

        switch (content)
        {
            case JsonObject obj:
                var properties = Properties(schema, obj, defs);
                foreach (var (key, value) in obj)
                {
                    var child = properties.GetValueOrDefault(key) ?? schema["additionalProperties"] as JsonObject;
                    Check(child, value, $"{path}/{Escape(key)}", defs, parameters, issues);
                }
                break;

            case JsonArray array when schema["items"] is JsonObject items:
                for (var i = 0; i < array.Count; i++)
                    Check(items, array[i], $"{path}/{i}", defs, parameters, issues);
                break;
        }
    }

    private void CheckReference(string name, JsonObject numeric, string path,
        JsonObject parameters, List<ValidationIssue> issues)
    {
        // Tham số chưa khai báo đã do ParamReferenceRule báo
        if (parameters[name] is not JsonObject definition) return;

        var empty = new JsonObject();
        if (!ScenarioContentReader.TryGetNumber(definition["value"], empty, out var value) ||
            !ScenarioContentReader.TryGetNumber(definition["min"], empty, out var min) ||
            !ScenarioContentReader.TryGetNumber(definition["max"], empty, out var max) ||
            !ScenarioContentReader.TryGetNumber(numeric["minimum"], empty, out var low) ||
            !ScenarioContentReader.TryGetNumber(numeric["maximum"], empty, out var high))
            return;

        if (ScenarioContentReader.AsString(numeric["type"]) == "integer" &&
            (ScenarioContentReader.AsString(definition["type"]) != "integer" || value != Math.Floor(value)))
            issues.Add(ValidationIssue.Error(Tier, path,
                $"Parameter '{name}' is used by an integer field and must have type 'integer' with a whole value."));
        else if (value < low || value > high)
            issues.Add(ValidationIssue.Error(Tier, path,
                string.Create(CultureInfo.InvariantCulture, $"Parameter '{name}' has value {value}, but this field accepts {low} to {high}.")));
        else if (min < low || max > high)
            issues.Add(ValidationIssue.Error(Tier, path,
                string.Create(CultureInfo.InvariantCulture, $"Parameter '{name}' allows {min} to {max}, but this field accepts {low} to {high}.")));
    }

    private static bool IsParamRefBranch(JsonObject branch)
        => ScenarioContentReader.AsString(branch["$ref"]) == ParamRefPointer;

    private static JsonObject? Resolve(JsonObject? schema, JsonObject defs)
    {
        while (schema is not null && ScenarioContentReader.AsString(schema["$ref"]) is { } reference)
            schema = defs[reference.Split('/')[^1]] as JsonObject;
        return schema;
    }

    /// <summary>Chọn nhánh oneOf của object: khớp "type" const, nếu không thì nhánh có đủ trường bắt buộc.</summary>
    private static JsonObject? Choose(IEnumerable<JsonObject?> branches, JsonObject content)
    {
        var candidates = branches.OfType<JsonObject>().ToList();
        var type = ScenarioContentReader.AsString(content["type"]);
        return candidates.FirstOrDefault(b =>
                   type is not null &&
                   ScenarioContentReader.AsString((b["properties"] as JsonObject)?["type"]?["const"]) == type)
               ?? candidates.FirstOrDefault(b =>
                   (b["required"] as JsonArray ?? []).All(k => ScenarioContentReader.AsString(k) is { } key && content.ContainsKey(key)));
    }

    /// <summary>properties của object, cộng thêm then.properties của các nhánh allOf/if khớp (ghi đè).</summary>
    private static Dictionary<string, JsonObject> Properties(JsonObject schema, JsonObject content, JsonObject defs)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        Merge(schema["properties"] as JsonObject, result);

        foreach (var branch in (schema["allOf"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (branch["if"]?["properties"] is not JsonObject conditions) continue;
            var matches = conditions.All(condition =>
            {
                var actual = ScenarioContentReader.AsString(content[condition.Key]);
                if (actual is null || condition.Value is not JsonObject rule) return false;
                if (rule["const"] is { } constant) return ScenarioContentReader.AsString(constant) == actual;
                return rule["enum"] is JsonArray options && options.Any(o => ScenarioContentReader.AsString(o) == actual);
            });
            if (matches) Merge(Resolve(branch["then"] as JsonObject, defs)?["properties"] as JsonObject, result);
        }
        return result;
    }

    private static void Merge(JsonObject? properties, Dictionary<string, JsonObject> target)
    {
        foreach (var (key, value) in properties ?? [])
            if (value is JsonObject schema) target[key] = schema;
    }

    private static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");
}
