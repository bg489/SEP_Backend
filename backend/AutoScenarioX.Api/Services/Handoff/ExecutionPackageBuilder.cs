using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.DTOs.Handoff;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Maps;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Services.Handoff;

/// <summary>Kết quả dựng gói chạy từ nội dung một version.</summary>
public sealed record ExecutionPackageContent(
    JsonObject ResolvedContent,
    IReadOnlyDictionary<string, JsonElement> ParameterValues,
    IReadOnlyList<ResolvedLocation> Locations,
    IReadOnlyList<string> Issues);

/// <summary>
/// Dựng phần nội dung của gói chạy cho Flow 2 (không đụng DB, dễ kiểm thử):
/// (1) thay mọi {"$param"} bằng parameters.*.value, (2) đổi từng spawn_point_index ra toạ độ
/// theo map metadata đang active. Version cũ có thể được publish trước khi nạp map hoặc map
/// metadata đổi sau đó, nên thiếu dữ liệu thì ghi vào Issues thay vì ném lỗi.
/// </summary>
public static class ExecutionPackageBuilder
{
    public static ExecutionPackageContent Build(JsonObject content, MapMetadata? map)
    {
        var issues = new List<string>();
        var parameters = ScenarioContentReader.Parameters(content);
        var resolved = (JsonObject)content.DeepClone();

        var missing = ScenarioContentReader.WalkObjects(content)
            .Select(x => (x.Path, Name: ScenarioContentReader.AsParamRef(x.Node)))
            .Where(x => x.Name is not null && (parameters[x.Name] as JsonObject)?["value"] is null)
            .ToList();
        foreach (var (path, name) in missing)
            issues.Add($"{path}: parameter '{name}' is not declared or has no value.");
        if (missing.Count == 0)
            ScenarioContentReader.ResolveParamReferences(resolved, parameters);

        var parameterValues = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, definition) in parameters)
            if (definition?["value"] is { } value)
                parameterValues[name] = JsonSerializer.SerializeToElement(value);

        var mapName = ScenarioContentReader.AsString(resolved["map"]?["name"]);
        var spawnPoints = map is null ? null : SpawnPointReader.Parse(map.SpawnPoints);
        if (map is null)
            issues.Add($"No active CARLA map metadata for map '{mapName}'. Spawn point indices cannot be converted to coordinates.");

        var locations = new List<ResolvedLocation>();
        foreach (var (path, node) in ScenarioContentReader.WalkObjects(resolved))
        {
            if (!node.ContainsKey("spawn_point_index")) continue;
            if (!ScenarioContentReader.TryGetNumber(node["spawn_point_index"], new JsonObject(), out var raw)
                || raw < 0 || raw != Math.Floor(raw) || raw > int.MaxValue)
            {
                issues.Add($"{path}/spawn_point_index: value is not a resolved non-negative integer.");
                continue;
            }

            var index = (int)raw;
            SpawnTransform? transform = null;
            if (spawnPoints is not null && !spawnPoints.TryGetValue(index, out transform))
                issues.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{path}/spawn_point_index: index {index} is out of range for map '{map!.MapName}' ({map.SpawnPointCount} spawn points)."));
            locations.Add(new ResolvedLocation(path, index, transform));
        }

        return new ExecutionPackageContent(resolved, parameterValues, locations, issues);
    }
}
