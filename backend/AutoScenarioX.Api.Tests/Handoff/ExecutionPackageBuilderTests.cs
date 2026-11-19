using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.Services.Handoff;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Handoff;

public class ExecutionPackageBuilderTests
{
    private static JsonObject S23() => ScenarioContentNormalizer.Normalize(TestData.LoadExample("S23_pedestrian_crossing_rain.json"));

    private static JsonObject SpawnPoint(string map, int index)
        => JsonNode.Parse(TestData.Map(map).SpawnPoints)!.AsArray()[index]!.AsObject();

    [Fact]
    public void Resolves_parameters_without_touching_the_source()
    {
        var content = S23();
        var before = content.ToJsonString();

        var package = ExecutionPackageBuilder.Build(content, TestData.Map("Town05"));

        Assert.Equal(before, content.ToJsonString());
        Assert.DoesNotContain("$param", package.ResolvedContent.ToJsonString());
        Assert.Equal(10, package.ResolvedContent["events"]![0]!["trigger"]!["distance_m"]!.GetValue<double>());
        Assert.Equal(1.4, package.ResolvedContent["events"]![0]!["params"]!["speed_ms"]!.GetValue<double>());
        Assert.Equal(20, package.ResolvedContent["traffic"]!["npc_vehicles"]!["count"]!.GetValue<int>());
        Assert.Equal(["npc_count", "pedestrian_speed_ms", "trigger_distance_m"], package.ParameterValues.Keys);
        Assert.Empty(package.Issues);
    }

    [Fact]
    public void Converts_spawn_indices_to_map_coordinates()
    {
        var package = ExecutionPackageBuilder.Build(S23(), TestData.Map("Town05"));

        var start = Assert.Single(package.Locations, l => l.Path == "/route/start");
        Assert.Equal(12, start.SpawnPointIndex);
        var expected = SpawnPoint("Town05", 12);
        Assert.Equal(expected["x"]!.GetValue<double>(), start.Transform!.X);
        Assert.Equal(expected["y"]!.GetValue<double>(), start.Transform.Y);
        Assert.Equal(expected["yaw"]!.GetValue<double>(), start.Transform.Yaw);
        Assert.Contains(package.Locations, l => l.Path == "/route/goal" && l.SpawnPointIndex == 87);
    }

    [Fact]
    public void Opt_map_uses_its_own_spawn_point_order()
    {
        var content = S23();
        content["map"]!["name"] = "Town05_Opt";

        var start = ExecutionPackageBuilder.Build(content, TestData.Map("Town05_Opt")).Locations.Single(l => l.Path == "/route/start");

        Assert.Equal(SpawnPoint("Town05_Opt", 12)["x"]!.GetValue<double>(), start.Transform!.X);
        Assert.NotEqual(SpawnPoint("Town05", 12)["x"]!.GetValue<double>(), start.Transform.X);
    }

    [Fact]
    public void Missing_map_metadata_is_reported_not_thrown()
    {
        var package = ExecutionPackageBuilder.Build(S23(), map: null);

        Assert.Contains(package.Issues, i => i.Contains("No active CARLA map metadata for map 'Town05'"));
        Assert.All(package.Locations, l => Assert.Null(l.Transform));
        Assert.Equal(2, package.Locations.Count);
    }

    [Fact]
    public void Out_of_range_index_is_reported()
    {
        var content = S23();
        content["route"]!["start"]!["spawn_point_index"] = 500;

        var package = ExecutionPackageBuilder.Build(content, TestData.Map("Town05"));

        var issue = Assert.Single(package.Issues);
        Assert.StartsWith("/route/start/spawn_point_index: index 500 is out of range", issue);
        Assert.Null(package.Locations.Single(l => l.Path == "/route/start").Transform);
    }

    [Fact]
    public void Undeclared_parameter_is_reported_and_left_unresolved()
    {
        var content = S23();
        content["events"]![0]!["params"]!["speed_ms"] = new JsonObject { ["$param"] = "ghost" };

        var package = ExecutionPackageBuilder.Build(content, TestData.Map("Town05"));

        Assert.Contains(package.Issues, i => i == "/events/0/params/speed_ms: parameter 'ghost' is not declared or has no value.");
        Assert.Contains("$param", package.ResolvedContent.ToJsonString());
    }

    [Fact]
    public void Spawn_index_given_by_parameter_is_resolved_first()
    {
        var content = S23();
        content["parameters"]!["start_index"] = new JsonObject { ["type"] = "integer", ["value"] = 3, ["min"] = 0, ["max"] = 10 };
        content["route"]!["start"]!["spawn_point_index"] = new JsonObject { ["$param"] = "start_index" };

        var start = ExecutionPackageBuilder.Build(content, TestData.Map("Town05")).Locations.Single(l => l.Path == "/route/start");

        Assert.Equal(3, start.SpawnPointIndex);
        Assert.Equal(SpawnPoint("Town05", 3)["x"]!.GetValue<double>(), start.Transform!.X);
    }
}
