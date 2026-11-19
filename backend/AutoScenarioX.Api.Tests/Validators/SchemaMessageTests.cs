using System.Text.Json.Nodes;
using AutoScenarioX.Api.Tests.Support;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Tests.Validators;

/// <summary>
/// Tầng 1 trả câu dễ hiểu thay cho thông điệp của JsonSchema.Net, path giữ nguyên (khớp bản Python).
/// Mỗi ca sửa đúng một chỗ trong S23 hợp lệ.
/// </summary>
public class SchemaMessageTests
{
    public static TheoryData<string, string, string> Cases => new()
    {
        { "event actor_ref empty", "/events/0/actor_ref", "Choose an actor." },
        { "trigger actor_ref empty", "/events/0/trigger", "Actor reference: choose an actor." },
        { "speed above schema maximum", "/events/0/params/speed_ms", "Must be at most 6." },
        { "empty parameter reference", "/events/0/params/speed_ms", "Parameter reference: choose a parameter." },
        { "negative spawn point", "/route/start", "Spawn point index: must be at least 0." },
        { "coordinates without y", "/route/start", "Y (m) is required." },
        { "unknown setting", "/route", "'foo' is not a recognised setting here." },
        { "missing goal", "/route", "Goal position is required." },
        { "direction outside enum", "/events/0/params/direction", "Choose one of: left_to_right, right_to_left." },
        { "text instead of number", "/route/speed_limit_kmh", "Enter a number." },
        { "unknown map name", "/map/name", "Use a CARLA map name such as Town03, Town10HD or Town05_Opt." },
        { "trigger time above maximum", "/events/0/trigger", "Time (s): must be at most 3600." },
        { "trigger without actor", "/events/0/trigger", "Actor reference is required." },
        { "unknown trigger type", "/events/0/trigger",
            "Type: choose one of: ego_distance_to_actor, ego_distance_to_location, simulation_time, after_event, time_to_collision." },
        { "text in number-or-parameter field", "/events/0/params/speed_ms", "Enter a number or choose a parameter." },
        { "invalid parameter name", "/parameters",
            "Parameter name 'Bad Name' is not valid. Start with a lowercase letter and use only lowercase letters, digits or '_' (up to 32 characters)." },
        { "zero step", "/parameters/npc_count/step", "Must be greater than 0." },
    };

    private static readonly Dictionary<string, Action<JsonObject>> Changes = new()
    {
        ["event actor_ref empty"] = c => c["events"]![0]!["actor_ref"] = "",
        ["trigger actor_ref empty"] = c => c["events"]![0]!["trigger"]!["actor_ref"] = "",
        ["speed above schema maximum"] = c => c["events"]![0]!["params"]!["speed_ms"] = 10,
        ["empty parameter reference"] = c => c["events"]![0]!["params"]!["speed_ms"] = new JsonObject { ["$param"] = "" },
        ["negative spawn point"] = c => c["route"]!["start"] = new JsonObject { ["spawn_point_index"] = -1 },
        ["coordinates without y"] = c => c["route"]!["start"] = new JsonObject { ["x"] = 1 },
        ["unknown setting"] = c => c["route"]!["foo"] = 1,
        ["missing goal"] = c => c["route"]!.AsObject().Remove("goal"),
        ["direction outside enum"] = c => c["events"]![0]!["params"]!["direction"] = "up",
        ["text instead of number"] = c => c["route"]!["speed_limit_kmh"] = "fast",
        ["unknown map name"] = c => c["map"]!["name"] = "Paris",
        ["trigger time above maximum"] = c => c["events"]![0]!["trigger"] =
            new JsonObject { ["type"] = "simulation_time", ["time_s"] = 5000 },
        ["trigger without actor"] = c => c["events"]![0]!["trigger"] =
            new JsonObject { ["type"] = "ego_distance_to_actor", ["distance_m"] = 5 },
        ["unknown trigger type"] = c => c["events"]![0]!["trigger"]!["type"] = "boom",
        ["text in number-or-parameter field"] = c => c["events"]![0]!["params"]!["speed_ms"] = "fast",
        ["invalid parameter name"] = c => c["parameters"]!["Bad Name"] = c["parameters"]!["npc_count"]!.DeepClone(),
        ["zero step"] = c => c["parameters"]!["npc_count"]!["step"] = 0,
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Schema_errors_are_explained_in_plain_words(string change, string path, string message)
    {
        var content = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        Changes[change](content);

        var result = await TestData.ValidateAsync(content);

        Assert.Equal(ValidationTier.Schema, result.StoppedAt);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.Path);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task Traffic_light_event_with_actor_says_to_remove_it()
    {
        var content = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        content["events"]![0] = new JsonObject
        {
            ["id"] = "ev_light", ["type"] = "traffic_light_override", ["actor_ref"] = "ped_01",
            ["trigger"] = new JsonObject { ["type"] = "simulation_time", ["time_s"] = 5 },
            ["params"] = new JsonObject { ["state"] = "red", ["duration_s"] = 10 }
        };

        var result = await TestData.ValidateAsync(content);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Remove Actor reference: not used for this type.", error.Message);
    }
}
