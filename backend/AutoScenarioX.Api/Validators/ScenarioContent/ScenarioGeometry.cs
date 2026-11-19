using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.DTOs.Handoff;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Maps;

namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>Vị trí đã giải ra toạ độ CARLA (mét). SpawnIndex có giá trị khi vị trí khai báo bằng spawn point.</summary>
public readonly record struct ScenarioPosition(double X, double Y, double Z, int? SpawnIndex);

/// <summary>
/// Hình học dùng chung cho các luật tầng 3: giải vị trí (spawn point hoặc toạ độ) và đo khoảng cách.
/// Bản Python tham chiếu (tools/validate_scenario.py) cài đúng các phép tính này theo cùng thứ tự,
/// để kết quả và thông điệp hai bên khớp từng chữ.
/// </summary>
public static class ScenarioGeometry
{
    private static readonly ConditionalWeakTable<MapMetadata, IReadOnlyDictionary<int, SpawnTransform>> Cache = new();

    /// <summary>Spawn point của map (index → transform), đọc một lần cho mỗi bản MapMetadata.</summary>
    public static IReadOnlyDictionary<int, SpawnTransform> SpawnPoints(MapMetadata map)
        => Cache.GetValue(map, m => SpawnPointReader.Parse(m.SpawnPoints));

    /// <summary>
    /// Các vị trí khai báo trong scenario kèm JSON Pointer và chủ ngữ dùng trong thông điệp.
    /// Thứ tự: start, waypoint, goal, spawn của actor, rồi theo từng event: vị trí trigger, đích và điểm đồng bộ của vehicle_crossing.
    /// </summary>
    public static IEnumerable<(string Path, string Subject, JsonNode? Location)> Locations(JsonObject root)
    {
        if (root["route"] is JsonObject route)
        {
            yield return ("/route/start", "The route start", route["start"]);
            if (route["waypoints"] is JsonArray waypoints)
                for (var i = 0; i < waypoints.Count; i++)
                    yield return ($"/route/waypoints/{i}", $"Waypoint {i + 1}", waypoints[i]);
            yield return ("/route/goal", "The route goal", route["goal"]);
        }

        var actors = ScenarioContentReader.Actors(root);
        for (var i = 0; i < actors.Count; i++)
            if (actors[i] is JsonObject actor)
                yield return ($"/actors/{i}/spawn", $"Actor '{ScenarioContentReader.AsString(actor["id"])}'", actor["spawn"]);

        var events = ScenarioContentReader.Events(root);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not JsonObject evt) continue;
            if (evt["trigger"] is JsonObject trigger && trigger.ContainsKey("location"))
                yield return ($"/events/{i}/trigger/location",
                    $"The trigger location of event '{ScenarioContentReader.AsString(evt["id"])}'", trigger["location"]);
            if (evt["params"] is JsonObject parameters && parameters.ContainsKey("destination"))
                yield return ($"/events/{i}/params/destination",
                    $"The destination of event '{ScenarioContentReader.AsString(evt["id"])}'", parameters["destination"]);
            if (evt["params"]?["sync"] is JsonObject sync && sync.ContainsKey("point"))
                yield return ($"/events/{i}/params/sync/point",
                    $"The sync point of event '{ScenarioContentReader.AsString(evt["id"])}'", sync["point"]);
        }
    }

    /// <summary>
    /// Giải một vị trí: spawn_point_index tra trong metadata, toạ độ lấy x/y/z (z mặc định 0.5 như bộ chuẩn hoá).
    /// Trả false khi thiếu dữ liệu hoặc spawn point không có trên map (luật SpawnPointIndexRule báo lỗi đó).
    /// </summary>
    public static bool TryResolve(JsonNode? location, JsonObject parameters,
        IReadOnlyDictionary<int, SpawnTransform> points, out ScenarioPosition position)
    {
        position = default;
        if (location is not JsonObject node) return false;

        if (node.ContainsKey("spawn_point_index"))
        {
            if (!ScenarioContentReader.TryGetNumber(node["spawn_point_index"], parameters, out var raw)) return false;
            var index = (int)raw;
            if (index != raw || !points.TryGetValue(index, out var point)) return false;
            position = new ScenarioPosition(point.X, point.Y, point.Z, index);
            return true;
        }

        if (!ScenarioContentReader.TryGetNumber(node["x"], parameters, out var x) ||
            !ScenarioContentReader.TryGetNumber(node["y"], parameters, out var y))
            return false;
        if (!ScenarioContentReader.TryGetNumber(node["z"], parameters, out var z)) z = 0.5;
        position = new ScenarioPosition(x, y, z, null);
        return true;
    }

    /// <summary>Khoảng cách ngang (nhìn từ trên xuống), dùng khi so với mạng đường và đo độ dài đường đi tối thiểu.</summary>
    public static double Distance2D(ScenarioPosition a, ScenarioPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Khoảng cách 3D giữa hai vị trí của scenario, như carla.Location.distance.</summary>
    public static double Distance3D(ScenarioPosition a, ScenarioPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Làm tròn 1 chữ số thập phân bằng Math.Round (half-even) trên value×10 - Python dùng round(value*10)/10.</summary>
    public static double Round1(double value) => Math.Round(value * 10) / 10;

    /// <summary>In số như Python fmt_num: 15 → "15", 2.5 → "2.5".</summary>
    public static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
