using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.Services.Formats;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Formats;

/// <summary>Vòng export → import phải giữ nguyên content_hash (B11 từng mất kiểu "1.0" và số 1.0 qua YAML).</summary>
public class FormatAdapterTests
{
    private static readonly CanonicalJsonHasher Hasher = new();
    private static string Hash(JsonObject c) => Hasher.ComputeHash(ScenarioContentNormalizer.Normalize(c));

    public static TheoryData<IScenarioFormatAdapter> Adapters => new()
    {
        new JsonScenarioFormatAdapter(), new YamlScenarioFormatAdapter()
    };

    [Theory]
    [MemberData(nameof(Adapters))]
    public void Round_trip_keeps_content_hash(IScenarioFormatAdapter adapter)
    {
        foreach (var file in new[] { "S23_pedestrian_crossing_rain.json", "S31_cut_in_imported.json", "S24_pedestrian_night_fog.yaml" })
        {
            var original = TestData.LoadExample(file);
            var reloaded = adapter.Parse(adapter.Serialize(original));
            Assert.Equal(Hash(original), Hash(reloaded));
        }
    }

    [Fact]
    public void Yaml_keeps_schema_version_string_and_decimal_literal()
    {
        var adapter = new YamlScenarioFormatAdapter();
        var content = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        content["parameters"]!["pedestrian_speed_ms"]!["max"] = JsonNode.Parse("3.0");
        var reloaded = adapter.Parse(adapter.Serialize(content));

        Assert.Equal("1.0", reloaded["schema_version"]!.GetValue<string>());
        Assert.Equal("3.0", reloaded["parameters"]!["pedestrian_speed_ms"]!["max"]!.ToJsonString());
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("{ not json")]
    public void Json_adapter_rejects_non_object_or_invalid_text(string text)
        => Assert.Throws<ScenarioFormatException>(() => new JsonScenarioFormatAdapter().Parse(text));

    [Theory]
    [InlineData("- a\n- b")]
    [InlineData("key: [unclosed")]
    public void Yaml_adapter_rejects_non_mapping_or_invalid_text(string text)
        => Assert.Throws<ScenarioFormatException>(() => new YamlScenarioFormatAdapter().Parse(text));

    [Fact]
    public void Yaml_alias_bomb_is_rejected_quickly()
    {
        // 330 byte, 6 tầng alias x10 = hơn 1 triệu node; trước khi chặn tốn ~800 MB RAM.
        var text = "l0: &l0 [x,x,x,x,x,x,x,x,x,x]\n";
        for (var i = 1; i <= 6; i++)
            text += $"l{i}: &l{i} [{string.Join(",", Enumerable.Repeat($"*l{i - 1}", 10))}]\n";

        var error = Assert.Throws<ScenarioFormatException>(() => new YamlScenarioFormatAdapter().Parse(text));
        Assert.Contains("aliases", error.Message);
    }

    [Fact]
    public void Yaml_small_aliases_still_work()
    {
        var content = new YamlScenarioFormatAdapter().Parse("a: &p {x: 1}\nb: *p\n");
        Assert.Equal(content["a"]!.ToJsonString(), content["b"]!.ToJsonString());
    }
}
