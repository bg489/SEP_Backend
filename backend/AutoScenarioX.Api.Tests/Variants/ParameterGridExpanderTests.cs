using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Variants;
using AutoScenarioX.Api.Tests.Support;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Tests.Variants;

public class ParameterGridExpanderTests
{
    // S23: npc_count integer 0..60, pedestrian_speed_ms 0.5..3, trigger_distance_m 5..30
    private static JsonObject Parameters()
        => ScenarioContentReader.Parameters(TestData.LoadExample("S23_pedestrian_crossing_rain.json"));

    private static Dictionary<string, JsonElement> Grid(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static string FieldOf(Action action)
        => Assert.Single(Assert.Throws<RequestValidationException>(action).Errors.Keys);

    [Fact]
    public void Sv5_grid_expands_to_cartesian_product_in_ordinal_order()
    {
        var combos = ParameterGridExpander.Expand(
            Grid("""{"trigger_distance_m":[5,15,30],"npc_count":[0,40],"pedestrian_speed_ms":[0.8,2.0]}"""),
            Parameters(), 100);

        Assert.Equal(12, combos.Count);
        Assert.Equal(12, combos.Select(c => c.ToJsonString()).Distinct().Count());
        // Tham số sắp ordinal: npc_count đổi chậm nhất, trigger_distance_m nhanh nhất
        Assert.Equal("""{"npc_count":0,"pedestrian_speed_ms":0.8,"trigger_distance_m":5}""", combos[0].ToJsonString());
        Assert.Equal("""{"npc_count":40,"pedestrian_speed_ms":2.0,"trigger_distance_m":30}""", combos[^1].ToJsonString());
    }

    [Fact]
    public void Weather_axis_combines_with_parameters()
    {
        // Euro NCAP thử người đi bộ cả ngày lẫn đêm: 2 preset × 2 tốc độ
        var combos = ParameterGridExpander.Expand(
            Grid("""{"weather.preset":["ClearNoon","ClearNight"],"pedestrian_speed_ms":[1.4,2.2]}"""), Parameters(), 100);
        Assert.Equal(4, combos.Count);
        Assert.Equal("""{"pedestrian_speed_ms":1.4,"weather.preset":"ClearNoon"}""", combos[0].ToJsonString());
    }

    [Theory]
    [InlineData("""{"weather.preset":["Sunny"]}""", "parameterGrid.weather.preset[0]")]
    [InlineData("""{"weather.preset":[1]}""", "parameterGrid.weather.preset[0]")]
    [InlineData("""{"weather.preset":["ClearNoon","ClearNoon"]}""", "parameterGrid.weather.preset")]
    public void Weather_axis_takes_unique_catalog_presets(string grid, string field)
        => Assert.Equal(field, FieldOf(() => ParameterGridExpander.Expand(Grid(grid), Parameters(), 100)));

    [Fact]
    public void Number_literal_is_kept_verbatim()
        => Assert.Equal("2.0", ParameterGridExpander.Expand(Grid("""{"pedestrian_speed_ms":[2.0]}"""), Parameters(), 100)[0]["pedestrian_speed_ms"]!.ToJsonString());

    [Fact]
    public void Empty_grid_is_rejected()
        => Assert.Equal("parameterGrid", FieldOf(() => ParameterGridExpander.Expand(Grid("{}"), Parameters(), 100)));

    [Fact]
    public void Undeclared_parameter_is_rejected()
        => Assert.Equal("parameterGrid.ghost", FieldOf(() => ParameterGridExpander.Expand(Grid("""{"ghost":[1]}"""), Parameters(), 100)));

    [Fact]
    public void Empty_axis_is_rejected()
        => Assert.Equal("parameterGrid.npc_count", FieldOf(() => ParameterGridExpander.Expand(Grid("""{"npc_count":[]}"""), Parameters(), 100)));

    [Theory]
    [InlineData("""{"npc_count":[61]}""", "parameterGrid.npc_count[0]")]          // > max
    [InlineData("""{"npc_count":[10,-1]}""", "parameterGrid.npc_count[1]")]       // < min
    [InlineData("""{"npc_count":[2.5]}""", "parameterGrid.npc_count[0]")]         // integer
    [InlineData("""{"npc_count":["10"]}""", "parameterGrid.npc_count[0]")]        // không phải số
    [InlineData("""{"pedestrian_speed_ms":[1,1.0]}""", "parameterGrid.pedestrian_speed_ms")] // trùng
    public void Invalid_values_are_rejected_with_field_path(string grid, string field)
        => Assert.Equal(field, FieldOf(() => ParameterGridExpander.Expand(Grid(grid), Parameters(), 100)));

    [Fact]
    public void Batch_size_limit_is_enforced()
    {
        Assert.Equal(4, ParameterGridExpander.Expand(Grid("""{"npc_count":[0,10],"trigger_distance_m":[5,10]}"""), Parameters(), 4).Count);
        Assert.Equal("parameterGrid", FieldOf(() =>
            ParameterGridExpander.Expand(Grid("""{"npc_count":[0,10],"trigger_distance_m":[5,10,15]}"""), Parameters(), 5)));
    }

    // ---- pairwise ----

    /// <summary>4 tham số × 3 giá trị (81 tổ hợp đủ) dựng từ min/max để không phụ thuộc file ví dụ.</summary>
    private static JsonObject FourParameters() => JsonNode.Parse("""
        {"a":{"type":"integer","value":1,"min":0,"max":9},"b":{"type":"integer","value":1,"min":0,"max":9},
         "c":{"type":"integer","value":1,"min":0,"max":9},"d":{"type":"integer","value":1,"min":0,"max":9}}
        """)!.AsObject();

    private static IReadOnlyList<JsonObject> Pairwise(string grid, JsonObject parameters, int max = 100)
        => ParameterGridExpander.Expand(Grid(grid), parameters, max, VariantStrategies.Pairwise);

    private static void AssertAllPairsCovered(Dictionary<string, JsonElement> grid, IReadOnlyList<JsonObject> rows)
    {
        var names = grid.Keys.ToArray();
        for (var i = 0; i < names.Length; i++)
            for (var j = i + 1; j < names.Length; j++)
                foreach (var x in grid[names[i]].EnumerateArray())
                    foreach (var y in grid[names[j]].EnumerateArray())
                        Assert.Contains(rows, r => r[names[i]]!.ToJsonString() == x.GetRawText()
                                                   && r[names[j]]!.ToJsonString() == y.GetRawText());
    }

    [Theory]
    [InlineData("""{"a":[0,1,2],"b":[0,1,2],"c":[0,1,2],"d":[0,1,2]}""", 10, 81)]   // cận dưới 9
    [InlineData("""{"a":[0,1,2,3,4],"b":[0,1],"c":[0,1,2],"d":[0,1]}""", 16, 60)]          // cận dưới 5 × 3 = 15
    [InlineData("""{"a":[0,1],"b":[0,1],"c":[0,1],"d":[0,1]}""", 6, 16)]
    public void Pairwise_covers_every_pair_with_fewer_rows(string json, int expected, int cartesian)
    {
        var rows = Pairwise(json, FourParameters());
        AssertAllPairsCovered(Grid(json), rows);
        Assert.Equal(expected, rows.Count);          // khoá kết quả IPOG để phát hiện thay đổi thuật toán
        Assert.True(rows.Count < cartesian);
        Assert.Equal(rows.Count, rows.Select(r => r.ToJsonString()).Distinct().Count());
    }

    [Fact]
    public void Pairwise_on_sv5_grid_covers_all_pairs()
    {
        const string json = """{"trigger_distance_m":[5,15,30],"npc_count":[0,40],"pedestrian_speed_ms":[0.8,2.0]}""";
        var rows = Pairwise(json, Parameters());
        AssertAllPairsCovered(Grid(json), rows);
        Assert.Equal(6, rows.Count);                 // 3 × 2 là cận dưới; grid đủ là 12
        // Khoá key theo thứ tự ordinal giống cartesian
        Assert.All(rows, r => Assert.Equal(["npc_count", "pedestrian_speed_ms", "trigger_distance_m"], r.Select(p => p.Key)));
    }

    [Fact]
    public void Pairwise_is_deterministic()
    {
        const string json = """{"a":[0,1,2,3],"b":[0,1,2],"c":[0,1,2],"d":[0,1]}""";
        var first = Pairwise(json, FourParameters()).Select(r => r.ToJsonString());
        var second = Pairwise(json, FourParameters()).Select(r => r.ToJsonString());
        Assert.Equal(first, second);
    }

    [Fact]
    public void Pairwise_with_one_or_two_parameters_equals_cartesian()
    {
        foreach (var json in new[] { """{"a":[0,1,2]}""", """{"a":[0,1,2],"b":[3,4]}""" })
            Assert.Equal(
                ParameterGridExpander.Expand(Grid(json), FourParameters(), 100).Select(r => r.ToJsonString()),
                Pairwise(json, FourParameters()).Select(r => r.ToJsonString()));
    }

    [Fact]
    public void Pairwise_allows_grids_whose_cartesian_product_exceeds_the_limit()
    {
        const string json = """{"a":[0,1,2,3,4],"b":[0,1,2,3,4],"c":[0,1,2,3,4],"d":[0,1,2,3,4]}""";  // 625 tổ hợp đủ
        Assert.Equal("parameterGrid", FieldOf(() => ParameterGridExpander.Expand(Grid(json), FourParameters(), 100)));
        var rows = Pairwise(json, FourParameters());
        AssertAllPairsCovered(Grid(json), rows);
        Assert.Equal(32, rows.Count);                // cận dưới 25
    }

    [Fact]
    public void Pairwise_limit_uses_the_two_largest_axes()
        => Assert.Equal("parameterGrid", FieldOf(() => Pairwise("""{"a":[0,1,2,3],"b":[0,1,2],"c":[0,1]}""", FourParameters(), 11)));

    [Fact]
    public void Unknown_strategy_is_rejected()
        => Assert.Equal("strategy", FieldOf(() =>
            ParameterGridExpander.Expand(Grid("""{"npc_count":[0]}"""), Parameters(), 100, "random")));
}
