using System.Text.Json.Nodes;
using AutoScenarioX.Api.Services.Maps;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Maps;

public class SpawnPointReaderTests
{
    [Theory]
    [InlineData("Town01")]
    [InlineData("Town05")]
    [InlineData("Town10HD_Opt")]
    public void Every_exported_spawn_point_is_read(string name)
    {
        var map = TestData.Map(name);
        Assert.Equal(map.SpawnPointCount, SpawnPointReader.Parse(map.SpawnPoints).Count);
    }

    [Fact]
    public void Coordinates_match_the_exported_file()
    {
        var map = TestData.Map("Town05");
        var file = JsonNode.Parse(map.SpawnPoints)!.AsArray().OfType<JsonObject>()
            .First(p => p["index"]!.GetValue<int>() == 12);
        var point = SpawnPointReader.Parse(map.SpawnPoints)[12];
        Assert.Equal(file["x"]!.GetValue<double>(), point.X);
        Assert.Equal(file["y"]!.GetValue<double>(), point.Y);
        Assert.Equal(file["yaw"]!.GetValue<double>(), point.Yaw);
    }

    [Fact]
    public void Entries_without_index_are_skipped()
        => Assert.Equal([3], SpawnPointReader.Parse("""[{"x":1,"y":2},{"index":3,"x":4,"y":5,"z":0,"yaw":90},"bad"]""").Keys);

    [Fact]
    public void Non_array_yields_no_points()
        => Assert.Empty(SpawnPointReader.Parse("{}"));
}
