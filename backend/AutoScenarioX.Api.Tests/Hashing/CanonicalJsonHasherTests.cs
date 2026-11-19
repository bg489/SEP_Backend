using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Hashing;

public class CanonicalJsonHasherTests
{
    private static readonly CanonicalJsonHasher Hasher = new();

    private static string Hash(JsonObject content) => Hasher.ComputeHash(ScenarioContentNormalizer.Normalize(content));

    /// <summary>Hash do tools/validate_scenario.py tính (normalize + compute_content_hash), 2026-10-08.</summary>
    [Theory]
    [InlineData("S23_pedestrian_crossing_rain.json", "sha256:1046193411b48c52f256cd924fd02330e3796f3283d2babb0c9e574d890a1484")]
    [InlineData("S31_cut_in_imported.json", "sha256:ec8566cd93219e83dfa6f78ae2ca0dcc4e0733c9466111ec1d4c734dbe28795f")]
    [InlineData("S24_pedestrian_night_fog.yaml", "sha256:e20267b0da5871329ef327097f1e80b389b9939435aa846d23e679a126768722")]
    [InlineData("S23_town05_opt_boundary.json", "sha256:3bae78b944045e048491529f8219edc43679ba7e8190bf7be8ac7d227df4abc7")]
    public void Hash_matches_python_reference(string file, string expected)
        => Assert.Equal(expected, Hash(TestData.LoadExample(file)));

    [Fact]
    public void Metadata_and_source_do_not_affect_hash()
    {
        var original = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        var renamed = (JsonObject)original.DeepClone();
        renamed["metadata"]!["name"] = "Another name";
        renamed["metadata"]!["tags"] = new JsonArray("x");
        renamed["source"] = new JsonObject { ["type"] = "manual" };
        Assert.Equal(Hash(original), Hash(renamed));
    }

    [Fact]
    public void Key_order_does_not_affect_hash()
    {
        var original = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        var reversed = new JsonObject();
        foreach (var (key, value) in original.Reverse().ToList())
            reversed[key] = value?.DeepClone();
        Assert.Equal(Hash(original), Hash(reversed));
    }

    [Fact]
    public void Filled_defaults_give_same_hash_as_explicit_values()
    {
        // Normalizer điền default theo schema: bỏ trống hay ghi rõ giá trị default phải cùng hash.
        var original = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        var explicitDefaults = ScenarioContentNormalizer.Normalize(original);
        Assert.Equal(Hash(original), Hash(explicitDefaults));
    }

    [Fact]
    public void Execution_change_changes_hash()
    {
        var original = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        var changed = (JsonObject)original.DeepClone();
        changed["execution"]!["seed"] = 43;
        Assert.NotEqual(Hash(original), Hash(changed));
    }

    [Fact]
    public void Hash_format_is_prefixed_lowercase_sha256()
        => Assert.Matches("^sha256:[0-9a-f]{64}$", Hash(TestData.LoadExample("S31_cut_in_imported.json")));
}
