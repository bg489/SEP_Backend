using System.Text.Json.Nodes;
using AutoScenarioX.Api.DTOs.Handoff;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Services.Maps;

/// <summary>Đọc cột jsonb map_metadata.spawn_points ([{index, x, y, z, yaw}]) thành bảng index → transform.</summary>
public static class SpawnPointReader
{
    public static Dictionary<int, SpawnTransform> Parse(string json)
    {
        var result = new Dictionary<int, SpawnTransform>();
        if (JsonNode.Parse(json) is not JsonArray points) return result;

        foreach (var node in points.OfType<JsonObject>())
        {
            if (!ScenarioContentReader.TryGetNumber(node["index"], new JsonObject(), out var index)) continue;
            ScenarioContentReader.TryGetNumber(node["x"], new JsonObject(), out var x);
            ScenarioContentReader.TryGetNumber(node["y"], new JsonObject(), out var y);
            ScenarioContentReader.TryGetNumber(node["z"], new JsonObject(), out var z);
            ScenarioContentReader.TryGetNumber(node["yaw"], new JsonObject(), out var yaw);
            result[(int)index] = new SpawnTransform(x, y, z, yaw);
        }
        return result;
    }
}
