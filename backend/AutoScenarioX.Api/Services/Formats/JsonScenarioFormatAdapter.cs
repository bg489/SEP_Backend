using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Services.Formats;

/// <summary>Định dạng chuẩn chính: JSON đúng schema v1.0, không phải chuyển đổi gì.</summary>
public sealed class JsonScenarioFormatAdapter : IScenarioFormatAdapter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string Format => Common.Constants.ScenarioFormats.Json;
    public IReadOnlyList<string> Extensions => [".json"];
    public string DefaultExtension => ".json";
    public string MediaType => "application/json";

    public JsonObject Parse(string text)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ScenarioFormatException($"Invalid JSON syntax: {ex.Message}");
        }

        return node as JsonObject
            ?? throw new ScenarioFormatException("The file root must be a JSON object.");
    }

    public string Serialize(JsonObject content) => content.ToJsonString(WriteOptions);
}
