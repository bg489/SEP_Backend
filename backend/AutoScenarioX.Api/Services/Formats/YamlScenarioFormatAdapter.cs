using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoScenarioX.Api.Services.Formats;

/// <summary>
/// YAML &lt;-&gt; JSON nội bộ.
///
/// Điểm cần cẩn thận: YAML không có kiểu như JSON, nên chuyển đổi ẩu là mất kiểu
/// theo cả hai chiều. Adapter này phải bảo đảm export rồi import lại cho ra ĐÚNG
/// content_hash cũ, vì vậy:
///
///  * Đọc vào: chỉ suy kiểu cho scalar viết trần. Đã đặt trong nháy thì giữ nguyên
///    là chuỗi, nhờ vậy "40" không biến thành số 40.
///  * Ghi ra: giữ nguyên biểu diễn số của JSON (1.0 phải ra 1.0 chứ không rút thành 1,
///    nếu không canonical JSON đổi và content_hash lệch), và ép nháy những chuỗi mà
///    YAML sẽ đọc nhầm thành số/bool/null (schema_version "1.0" là ca điển hình).
///
/// Vì vậy dùng RepresentationModel (kiểm soát được style từng scalar) cho cả hai
/// chiều, thay cho Serializer/Deserializer mức cao.
/// </summary>
public sealed class YamlScenarioFormatAdapter : IScenarioFormatAdapter
{
    public string Format => Common.Constants.ScenarioFormats.Yaml;
    public IReadOnlyList<string> Extensions => [".yaml", ".yml"];
    public string DefaultExtension => ".yaml";
    public string MediaType => "application/x-yaml";

    // ------------------------------------------------------------------
    // YAML -> JSON
    // ------------------------------------------------------------------
    public JsonObject Parse(string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            throw new ScenarioFormatException($"Invalid YAML syntax (line {ex.Start.Line}): {ex.Message}");
        }

        if (stream.Documents.Count == 0)
            throw new ScenarioFormatException("The YAML file is empty.");
        if (stream.Documents.Count > 1)
            throw new ScenarioFormatException(
                "The YAML file contains multiple documents separated by ---. Each file must contain a single scenario.");

        var budget = MaxNodes;
        return ToJson(stream.Documents[0].RootNode, ref budget) as JsonObject
            ?? throw new ScenarioFormatException("The file root must be a mapping (object).");
    }

    /// <summary>
    /// Trần số node khi đổi sang JSON. Alias YAML (&amp;a / *a) trỏ lại cùng một node nên lồng alias
    /// làm cây nở theo cấp số nhân ("billion laughs"): file vài trăm byte thành hàng GB RAM.
    /// Scenario thật chỉ vài nghìn node.
    /// </summary>
    public const int MaxNodes = 50_000;

    private static JsonNode? ToJson(YamlNode node, ref int budget)
    {
        if (--budget < 0)
            throw new ScenarioFormatException(
                $"The YAML file expands to more than {MaxNodes:N0} values (check anchors and aliases).");

        switch (node)
        {
            case YamlMappingNode mapping:
            {
                var result = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is not YamlScalarNode { Value: { } name })
                        throw new ScenarioFormatException("YAML keys must be plain strings.");
                    if (result.ContainsKey(name))
                        throw new ScenarioFormatException($"Duplicate key '{name}' in the same mapping.");
                    result[name] = ToJson(value, ref budget);
                }
                return result;
            }

            case YamlSequenceNode sequence:
            {
                var result = new JsonArray();
                foreach (var item in sequence.Children) result.Add(ToJson(item, ref budget));
                return result;
            }

            case YamlScalarNode scalar:
                return ToJsonScalar(scalar);

            default:
                throw new ScenarioFormatException($"Unsupported YAML structure: {node.NodeType}.");
        }
    }

    /// <summary>
    /// Suy kiểu theo YAML 1.2 core schema, nhưng CHỈ với scalar viết trần.
    /// Scalar có nháy luôn là chuỗi - nhờ vậy "1.0" vẫn là chuỗi.
    /// </summary>
    private static JsonNode? ToJsonScalar(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? string.Empty;

        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted
            or ScalarStyle.Literal or ScalarStyle.Folded)
            return JsonValue.Create(text);

        if (text.Length == 0 || text is "~" or "null" or "Null" or "NULL") return null;
        if (text is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (text is "false" or "False" or "FALSE") return JsonValue.Create(false);

        // Giữ nguyên biểu diễn số bằng cách parse lại qua JSON: "1.0" vào JSON vẫn là 1.0.
        if (LooksLikeNumber(text) && JsonNode.Parse(text) is JsonValue number) return number;

        return JsonValue.Create(text);
    }

    private static bool LooksLikeNumber(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
           || (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
               && !double.IsInfinity(d) && !double.IsNaN(d));

    // ------------------------------------------------------------------
    // JSON -> YAML
    // ------------------------------------------------------------------
    public string Serialize(JsonObject content)
    {
        var stream = new YamlStream(new YamlDocument(ToYaml(content)));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);

        // YamlStream.Save đóng document bằng "...", không cần thiết cho file một document.
        var text = writer.ToString();
        return text.EndsWith("...\n", StringComparison.Ordinal)
            ? text[..^4]
            : text.EndsWith("...", StringComparison.Ordinal) ? text[..^3] : text;
    }

    private static YamlNode ToYaml(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return Plain("null");

            case JsonObject obj:
            {
                var mapping = new YamlMappingNode();
                foreach (var (key, value) in obj) mapping.Add(ScalarForString(key), ToYaml(value));
                return mapping;
            }

            case JsonArray array:
            {
                var sequence = new YamlSequenceNode();
                foreach (var item in array) sequence.Add(ToYaml(item));
                return sequence;
            }

            case JsonValue value:
                return ToYamlScalar(value);

            default:
                return Plain(node.ToJsonString());
        }
    }

    private static YamlNode ToYamlScalar(JsonValue value)
    {
        if (value.TryGetValue<bool>(out var boolean)) return Plain(boolean ? "true" : "false");
        if (value.TryGetValue<string>(out var text)) return ScalarForString(text);

        // Số: ghi lại ĐÚNG chữ số như trong JSON. Nếu để YamlDotNet định dạng double
        // thì 1.0 ra "1", import lại thành số nguyên và content_hash lệch.
        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number)
            return Plain(element.GetRawText());

        return Plain(value.ToJsonString());
    }

    private static YamlScalarNode Plain(string text) => new(text) { Style = ScalarStyle.Plain };

    /// <summary>
    /// Chuỗi nào mà viết trần sẽ bị đọc lại thành số / bool / null thì phải ép nháy kép.
    /// Còn lại để emitter tự quyết (nó tự thêm nháy khi ký tự đặc biệt bắt buộc).
    /// </summary>
    private static YamlScalarNode ScalarForString(string text)
    {
        var ambiguous = text.Length == 0
                        || text is "~" or "null" or "Null" or "NULL"
                                or "true" or "True" or "TRUE"
                                or "false" or "False" or "FALSE"
                        || LooksLikeNumber(text)
                        || char.IsWhiteSpace(text[0])
                        || char.IsWhiteSpace(text[^1]);

        return new YamlScalarNode(text)
        {
            Style = ambiguous ? ScalarStyle.DoubleQuoted : ScalarStyle.Any
        };
    }
}
