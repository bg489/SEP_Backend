using AutoScenarioX.Api.Tests.Support;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Tests.Validators;

/// <summary>
/// Bộ ví dụ vàng trong examples/: kết quả phải khớp validator Python tham chiếu
/// (tools/validate_scenario.py --metadata-dir map_metadata, cập nhật 2026-10-09 khi thêm luật tầng 3).
/// So tầng dừng, path lỗi và path cảnh báo. Tầng 1 so theo TẬP path như compare_validators.py: lỗi do
/// thư viện JSON Schema sinh (JsonSchema.Net vs jsonschema) nên số lần lặp một path có thể khác. Tầng 2-3
/// do ta tự viết nên so đủ danh sách, kể cả path lặp.
/// </summary>
public class ExampleValidationTests
{
    public static TheoryData<string, string?, string[], string[]> Examples => new()
    {
        { "S23_pedestrian_crossing_rain.json", null, [], [] },
        { "S31_cut_in_imported.json", null, [], [] },
        { "S24_pedestrian_night_fog.yaml", null, [], [] },
        { "S23_town05_opt_boundary.json", null, [], [] },
        { "S41_lead_vehicle_brake.json", null, [], [] },
        { "S42_stopped_vehicle_ahead.json", null, [], [] },
        { "S43_slow_vehicle_ahead.json", null, [], [] },
        { "S51_red_light_intersection.json", null, [], [] },
        { "S52_crossing_vehicle_junction.json", null, [], [] },
        { "S53_left_turn_oncoming.json", null, [], [] },
        { "S54_vehicle_turning_into_path.json", null, [], [] },
        { "S55_same_direction_turn.json", null, [], [] },
        { "S61_speeding_open_road.json", null, [], [] },
        { "S25_pedestrian_crossing_ahead.json", null, [], [] },
        {
            "WARN_tier3_geometry.json", null, [],
            ["/actors/1/spawn", "/events/0/trigger/distance_m", "/execution/timeout_s"]
        },
        {
            "INVALID_tier1_schema.json", "schema",
            ["/ego_vehicle/blueprint", "/execution", "/map/name", "/metadata/category",
             "/route/speed_limit_kmh", "/source", "/source", "/weather/preset"],
            []
        },
        {
            "INVALID_tier2_business.json", "business",
            ["/actors/0/behavior", "/events/0/actor_ref", "/events/0/trigger/time_s",
             "/events/1/params/deceleration_ms2", "/events/1/trigger/actor_ref", "/parameters/decel/value",
             "/route/goal", "/traffic/npc_vehicles/count"],
            []
        },
        {
            "INVALID_tier2_param_target_range.json", "business",
            ["/events/0/params/speed_ms", "/events/1/params/duration_s",
             "/events/2/trigger/distance_m", "/traffic/npc_vehicles/count"],
            []
        },
        { "INVALID_tier3_map_not_loaded.json", "carla", ["/map/name"], [] },
        { "INVALID_tier3_npc_count.json", "carla", ["/traffic/npc_vehicles/count"], [] },
        { "INVALID_tier3_spawn_index.json", "carla", ["/route/start/spawn_point_index"], [] },
        { "INVALID_tier3_positions.json", "carla", ["/actors/0", "/actors/1/spawn", "/route/goal_tolerance_m"], [] },
        { "INVALID_tier3_route_timeout.json", "carla", ["/execution/timeout_s"], [] },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Example_matches_python_reference(string file, string? stoppedAt, string[] errorPaths, string[] warningPaths)
    {
        var result = await TestData.ValidateAsync(TestData.LoadExample(file));

        Assert.Equal(stoppedAt is null, result.IsValid);
        Assert.Equal(stoppedAt, result.StoppedAt?.ToDbValue());
        var actual = result.Errors.Select(e => e.Path).ToList();
        if (stoppedAt == "schema")
            Assert.Equal(errorPaths.ToHashSet(), actual.ToHashSet());
        else
            Assert.Equal(errorPaths.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.Equal(warningPaths.Order(StringComparer.Ordinal), result.Warnings.Select(w => w.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_example_file_is_covered()
    {
        var files = Directory.GetFiles(Path.Combine(TestData.RepoRoot, "examples"))
            .Select(Path.GetFileName)
            .Where(f => f!.EndsWith(".json") || f.EndsWith(".yaml"));
        var covered = Examples.Select(row => (string)row[0]);
        Assert.Equal(files.Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
    }
}
