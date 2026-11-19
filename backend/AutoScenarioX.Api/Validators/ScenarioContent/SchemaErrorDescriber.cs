using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>
/// Đổi lỗi kỹ thuật của JsonSchema.Net ("The string value is not a match for the indicated regular expression",
/// "Expected 1 matching subschema but found 0"...) thành câu tiếng Anh người dùng hiểu được.
/// Chỉ đổi thông điệp, KHÔNG đổi path: path tầng 1 vẫn phải khớp bản Python (compare_validators.py so (tier, path)).
/// Giá trị giới hạn (minimum, enum, pattern...) đọc thẳng từ schema theo EvaluationPath, nên sửa schema là thông điệp đổi theo.
/// </summary>
public sealed class SchemaErrorDescriber(JsonNode schemaRoot, JsonNode? instance, IReadOnlyList<EvaluationResults> failures)
{
    private static readonly string[] BranchKeywords = ["anyOf", "oneOf", "if", "not"];

    /// <summary>Nhãn khớp với nhãn ô nhập trên wizard (frontend SchemaFields.tsx).</summary>
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["schema_version"] = "Schema version", ["metadata"] = "Metadata", ["source"] = "Source", ["map"] = "Map",
        ["route"] = "Route", ["weather"] = "Weather", ["ego_vehicle"] = "Ego vehicle", ["traffic"] = "Traffic",
        ["actors"] = "Actors", ["events"] = "Events", ["parameters"] = "Parameters", ["execution"] = "Execution",
        ["name"] = "Name", ["category"] = "Category", ["tags"] = "Tags", ["format"] = "Format",
        ["original_hash"] = "Original hash", ["parent_version_id"] = "Parent version",
        ["start"] = "Start position", ["goal"] = "Goal position", ["waypoints"] = "Waypoints",
        ["spawn_point_index"] = "Spawn point index", ["speed_limit_kmh"] = "Speed limit (km/h)",
        ["goal_tolerance_m"] = "Goal tolerance (m)", ["preset"] = "Weather preset", ["blueprint"] = "Blueprint",
        ["role_name"] = "Role name", ["color"] = "RGB color", ["initial_speed_kmh"] = "Initial speed (km/h)", ["target_speed_kmh"] = "Target speed (km/h)",
        ["npc_vehicles"] = "NPC vehicles", ["pedestrians"] = "Background pedestrians", ["count"] = "Count",
        ["id"] = "ID", ["type"] = "Type", ["spawn"] = "Spawn position", ["behavior"] = "Behavior",
        ["actor_ref"] = "Actor reference", ["description"] = "Description", ["trigger"] = "Trigger",
        ["params"] = "Action parameters", ["distance_m"] = "Distance (m)", ["location"] = "Trigger target position",
        ["event_ref"] = "Starts after event", ["delay_s"] = "Delay after that event (s)", ["ttc_s"] = "Time to collision (s)", ["time_gap_s"] = "Time gap to the ego (s)",
        ["time_s"] = "Time (s)", ["speed_ms"] = "Speed (m/s)", ["direction"] = "Direction", ["side"] = "Cut-in side",
        ["cut_in_gap_m"] = "Cut-in gap (m)", ["lane_change_duration_s"] = "Lane change duration (s)",
        ["deceleration_ms2"] = "Deceleration (m/s²)", ["target_speed_kmh"] = "Target speed (km/h)",
        ["target"] = "Target traffic light", ["state"] = "Traffic light state", ["duration_s"] = "Duration (s)",
        ["destination"] = "Drive to", ["speed_kmh"] = "Speed (km/h)", ["ignore_traffic_lights"] = "Run red lights",
        ["sync"] = "Arrive together with the ego", ["point"] = "Conflict point", ["offset_s"] = "Arrival offset (s)",
        ["value"] = "Current value", ["min"] = "Minimum", ["max"] = "Maximum", ["step"] = "Suggested step",
        ["unit"] = "Unit", ["seed"] = "Random seed", ["timeout_s"] = "Timeout (s)",
        ["fixed_delta_seconds"] = "Simulation time step (s)", ["synchronous_mode"] = "Synchronous mode",
        ["warmup_s"] = "Warm-up time (s)", ["x"] = "X (m)", ["y"] = "Y (m)", ["z"] = "Z (m)", ["yaw"] = "Yaw (degrees)",
        ["$param"] = "Parameter reference"
    };

    /// <summary>Hướng dẫn cho từng pattern trong schema (regex thì người dùng không đọc được).</summary>
    private static readonly Dictionary<string, string> PatternHints = new(StringComparer.Ordinal)
    {
        ["^[a-z][a-z0-9_]{0,31}$"] = "Start with a lowercase letter and use only lowercase letters, digits or '_' (up to 32 characters).",
        ["^[a-z0-9][a-z0-9_-]{0,31}$"] = "Use up to 32 lowercase letters, digits, '_' or '-'.",
        ["^sha256:[a-f0-9]{64}$"] = "Use 'sha256:' followed by 64 lowercase hexadecimal characters.",
        ["^Town[0-9]{2}(HD)?(_Opt)?$"] = "Use a CARLA map name such as Town03, Town10HD or Town05_Opt.",
        ["^vehicle\\.[a-z0-9_]+\\.[a-z0-9_]+$"] = "Use a vehicle blueprint such as vehicle.tesla.model3.",
        ["^[0-9]{1,3},[0-9]{1,3},[0-9]{1,3}$"] = "Use three numbers from 0 to 255 separated by commas, for example 255,0,0.",
        ["^walker\\.pedestrian\\.[0-9]{4}$"] = "Use a pedestrian blueprint such as walker.pedestrian.0001.",
        ["^static\\.prop\\.[a-z0-9_]+$"] = "Use a static prop blueprint such as static.prop.streetbarrier."
    };

    /// <summary>Thông điệp cho một lỗi; issuePath là path sẽ trả về (lỗi nằm sâu hơn thì thêm nhãn trường bị lỗi).</summary>
    public string Describe(EvaluationResults node, string keyword, string rawMessage, string issuePath)
    {
        var message = DescribeKeyword(node, keyword, rawMessage);
        var path = Pointer(node.InstanceLocation);
        if (path == issuePath || path.Length <= issuePath.Length) return message;
        return $"{Label(LastSegment(path))}: {char.ToLowerInvariant(message[0])}{message[1..]}";
    }

    private string DescribeKeyword(EvaluationResults node, string keyword, string rawMessage)
    {
        var schema = SchemaAt(node.EvaluationPath);
        var value = InstanceAt(Pointer(node.InstanceLocation));
        var field = LastSegment(Pointer(node.InstanceLocation));

        switch (keyword)
        {
            case "oneOf":
            case "anyOf":
                return DescribeBranches(node);
            case "required":
                return DescribeRequired(schema, value);
            case "" when IsFalseSchema(schema):
                return $"'{field}' is not a recognised setting here.";
            case "type":
                return DescribeType(schema?["type"]);
            case "minimum":
                return $"Must be at least {Text(schema?["minimum"])}.";
            case "maximum":
                return $"Must be at most {Text(schema?["maximum"])}.";
            case "exclusiveMinimum":
                return $"Must be greater than {Text(schema?["exclusiveMinimum"])}.";
            case "exclusiveMaximum":
                return $"Must be less than {Text(schema?["exclusiveMaximum"])}.";
            case "enum":
                return $"Choose one of: {List(schema?["enum"])}.";
            case "const":
                return $"Must be {Text(schema?["const"])}.";
            case "pattern":
                return DescribePattern(node, schema, value, field);
            case "minLength":
                return IsEmptyString(value) ? "Cannot be empty." : $"Use at least {Text(schema?["minLength"])} characters.";
            case "maxLength":
                return $"Use at most {Text(schema?["maxLength"])} characters.";
            case "minItems":
                return $"Add at least {Text(schema?["minItems"])} item(s).";
            case "maxItems":
                return $"Use at most {Text(schema?["maxItems"])} items.";
            case "uniqueItems":
                return "Remove duplicate entries.";
            case "maxProperties":
                return $"Use at most {Text(schema?["maxProperties"])} entries.";
            case "format":
                return schema?["format"]?.GetValue<string>() == "uuid" ? "Enter a valid UUID." : "The value is not in the expected format.";
            case "not":
                return DescribeNegation(node) ?? "This value is not allowed here.";
            default:
                return rawMessage.Replace("\\u0022", "\"");
        }
    }

    /// <summary>
    /// Nút có "not": {"required": [...]} (vd event traffic_light_override không dùng actor_ref) mà tài liệu lại có
    /// các trường đó. JsonSchema.Net không sinh thông điệp cho trường hợp này nên rule gọi thẳng; null nếu không phải.
    /// </summary>
    public string? DescribeNegation(EvaluationResults node)
    {
        if (SchemaAt(node.EvaluationPath)?["not"]?["required"] is not JsonArray forbidden) return null;
        if (InstanceAt(Pointer(node.InstanceLocation)) is not JsonObject present) return null;
        var found = forbidden.Select(f => f!.GetValue<string>()).Where(present.ContainsKey).ToList();
        return found.Count == 0 ? null : $"Remove {Join(found.Select(Label), "and")}: not used for this type.";
    }

    /// <summary>
    /// oneOf/anyOf không khớp nhánh nào. Chọn nhánh người dùng nhiều khả năng muốn dùng: bỏ nhánh có const sai
    /// (vd trigger khác type) hoặc sai kiểu ngay tại gốc (vd nhánh {"$param"} khi người dùng nhập số), rồi lấy nhánh
    /// ít lỗi cấu trúc nhất và mô tả lỗi đầu tiên của nó.
    /// </summary>
    private string DescribeBranches(EvaluationResults node)
    {
        var basePath = node.EvaluationPath.ToString();
        var at = Pointer(node.InstanceLocation);
        var branches = failures
            .Select(f => (Node: f, Branch: BranchOf(basePath, f.EvaluationPath.ToString())))
            .Where(x => x.Branch is not null)
            .SelectMany(x => x.Node.Errors!.Select(e => (x.Node, Keyword: e.Key, Message: e.Value, Branch: x.Branch!)))
            .GroupBy(x => x.Branch)
            .Select(g => g.ToList())
            .ToList();

        var candidates = branches
            .Where(errors => !errors.Any(e => e.Keyword == "const" ||
                                              (e.Keyword == "type" && Pointer(e.Node.InstanceLocation) == at)))
            .OrderBy(errors => errors.Count(e => IsStructural(e.Keyword, e.Node)))
            .ToList();

        if (candidates.Count > 0)
        {
            var first = candidates[0].OrderBy(e => IsStructural(e.Keyword, e.Node) ? 1 : 0).First();
            return Describe(first.Node, first.Keyword, first.Message, at);
        }

        // Mọi nhánh đều sai const ở cùng một trường: đó là trường phân loại (vd trigger type), liệt kê giá trị từ schema
        var consts = branches.SelectMany(b => b).Where(e => e.Keyword == "const").ToList();
        if (consts.Count > 0 && consts.Select(e => Pointer(e.Node.InstanceLocation)).Distinct().Count() == 1)
        {
            var discriminator = LastSegment(Pointer(consts[0].Node.InstanceLocation));
            var allowed = (SchemaAt(node.EvaluationPath)?["oneOf"] as JsonArray ?? [])
                .Select(branch => Resolve(branch)?["properties"]?[discriminator]?["const"])
                .Where(value => value is not null)
                .Select(Text);
            return $"{Label(discriminator)}: choose one of: {string.Join(", ", allowed)}.";
        }

        // Mọi nhánh đều sai kiểu: thường là ô "số hoặc tham số"
        var types = branches.SelectMany(b => b).Where(e => e.Keyword == "type").ToList();
        if (types.Count > 0)
        {
            var allowsParam = types.Any(e => SchemaAt(e.Node.EvaluationPath)?["properties"]?["$param"] is not null);
            var expected = types
                .Select(e => SchemaAt(e.Node.EvaluationPath))
                .Where(s => s?["properties"]?["$param"] is null)
                .Select(s => TypeName(s?["type"]))
                .Distinct()
                .ToList();
            if (allowsParam && expected.Count > 0)
                return $"Enter {Join(expected, "or")} or choose a parameter.";
            if (expected.Count > 0)
                return $"Expected {Join(expected, "or")}.";
        }

        return "The value does not match any of the allowed forms here.";
    }

    /// <summary>Nhánh (vd ".../oneOf/2") chứa lỗi; null nếu lỗi không thuộc trực tiếp oneOf/anyOf này hoặc nằm trong nhánh con.</summary>
    private static string? BranchOf(string basePath, string evaluationPath)
    {
        foreach (var keyword in new[] { "/oneOf/", "/anyOf/" })
        {
            var prefix = basePath + keyword;
            if (!evaluationPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = evaluationPath[prefix.Length..].Split('/');
            // Lỗi trong nhánh con lồng bên trong (oneOf khác, if...) thì để oneOf con tự mô tả
            if (rest.Skip(1).Any(s => BranchKeywords.Contains(s, StringComparer.Ordinal))) return null;
            return prefix + rest[0];
        }
        return null;
    }

    private bool IsStructural(string keyword, EvaluationResults node)
        => keyword == "required" || (keyword == "" && IsFalseSchema(SchemaAt(node.EvaluationPath)));

    private static string DescribeRequired(JsonNode? schema, JsonNode? value)
    {
        var present = value as JsonObject;
        var missing = (schema?["required"] as JsonArray ?? [])
            .Select(n => n!.GetValue<string>())
            .Where(name => present is null || !present.ContainsKey(name))
            .Select(Label)
            .ToList();
        return missing.Count switch
        {
            0 => "A required setting is missing.",
            1 => $"{missing[0]} is required.",
            _ => $"{Join(missing, "and")} are required."
        };
    }

    private static string DescribeType(JsonNode? type) => TypeName(type) switch
    {
        "a whole number" => "Enter a whole number.",
        "a number" => "Enter a number.",
        "text" => "Enter text.",
        "true or false" => "Choose true or false.",
        var other => $"Expected {other}."
    };

    private static string DescribePattern(EvaluationResults node, JsonNode? schema, JsonNode? value, string field)
    {
        var pattern = schema?["pattern"]?.GetValue<string>() ?? "";
        var hint = PatternHints.GetValueOrDefault(pattern, "The value is not in the expected format.");

        // propertyNames: lỗi nằm ở chính tên khoá (tên tham số), không phải giá trị
        if (node.EvaluationPath.ToString().Contains("/propertyNames", StringComparison.Ordinal))
            return $"Parameter name '{field}' is not valid. {hint}";

        if (IsEmptyString(value))
            return field switch
            {
                "actor_ref" => "Choose an actor.",
                "$param" => "Choose a parameter.",
                _ => "Cannot be empty."
            };
        return hint;
    }

    private static string TypeName(JsonNode? type)
    {
        if (type is JsonArray many) return Join(many.Select(t => TypeName(t)), "or");
        return type?.GetValue<string>() switch
        {
            "integer" => "a whole number",
            "number" => "a number",
            "string" => "text",
            "boolean" => "true or false",
            "array" => "a list",
            "object" => "a group of settings",
            "null" => "nothing",
            _ => "a different kind of value"
        };
    }

    /// <summary>Đi theo EvaluationPath trong schema, nhảy qua $ref nội bộ (#/$defs/...).</summary>
    private JsonNode? SchemaAt(Json.Pointer.JsonPointer evaluationPath)
    {
        var node = schemaRoot;
        foreach (var segment in Segments(evaluationPath.ToString()))
        {
            node = segment == "$ref" && node?["$ref"]?.GetValue<string>() is { } reference && reference.StartsWith('#')
                ? Walk(schemaRoot, Segments(reference[1..]))
                : Child(node, segment);
            if (node is null) return null;
        }
        return node;
    }

    private JsonNode? Resolve(JsonNode? node)
        => node?["$ref"]?.GetValue<string>() is { } reference && reference.StartsWith('#')
            ? Walk(schemaRoot, Segments(reference[1..]))
            : node;

    private JsonNode? InstanceAt(string pointer) => Walk(instance, Segments(pointer));

    private static JsonNode? Walk(JsonNode? node, IEnumerable<string> segments)
    {
        foreach (var segment in segments)
        {
            node = Child(node, segment);
            if (node is null) return null;
        }
        return node;
    }

    private static JsonNode? Child(JsonNode? node, string segment) => node switch
    {
        JsonObject obj => obj[segment],
        JsonArray array when int.TryParse(segment, out var index) && index >= 0 && index < array.Count => array[index],
        _ => null
    };

    private static IEnumerable<string> Segments(string pointer)
        => pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Replace("~1", "/").Replace("~0", "~"));

    private static string Pointer(Json.Pointer.JsonPointer pointer)
    {
        var text = pointer.ToString();
        return string.IsNullOrEmpty(text) ? "/" : text;
    }

    private static string LastSegment(string pointer) => Segments(pointer).LastOrDefault() ?? "";

    /// <summary>Nhãn của trường; phần tử mảng ("/events/0") thành "Item 1".</summary>
    private static string Label(string segment)
        => Labels.TryGetValue(segment, out var label) ? label
            : int.TryParse(segment, out var index) ? $"Item {index + 1}"
            : segment.Replace('_', ' ');

    private static bool IsFalseSchema(JsonNode? schema)
        => schema is JsonValue v && v.GetValueKind() == JsonValueKind.False;

    private static bool IsEmptyString(JsonNode? value)
        => value is JsonValue v && v.GetValueKind() == JsonValueKind.String && string.IsNullOrEmpty(v.GetValue<string>());

    private static string Text(JsonNode? node) => node switch
    {
        null => "?",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => node.ToJsonString()
    };

    private static string List(JsonNode? node)
        => node is JsonArray array ? string.Join(", ", array.Select(Text)) : Text(node);

    private static string Join(IEnumerable<string> items, string conjunction)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : $"{string.Join(", ", list[..^1])} {conjunction} {list[^1]}";
    }
}
