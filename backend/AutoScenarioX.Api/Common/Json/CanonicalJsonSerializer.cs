using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Common.Json;

/// <summary>
/// Serialize JSON theo dạng chuẩn (canonical) dùng cho content_hash:
/// key sắp theo thứ tự ORDINAL, không khoảng thừa, UTF-8 giữ nguyên (không escape \uXXXX),
/// số giữ nguyên biểu diễn gốc (10 không thành 10.0).
/// Hai tài liệu tương đương về nội dung luôn cho ra cùng một chuỗi.
/// </summary>
public static class CanonicalJsonSerializer
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            Write(node, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;

            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array) Write(item, writer);
                writer.WriteEndArray();
                break;

            default:
                // JsonValue: WriteTo giữ nguyên biểu diễn gốc của số / chuỗi
                node.WriteTo(writer, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                break;
        }
    }
}
