using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Services.Tags;

namespace AutoScenarioX.Api.Tests.Tags;

public class TagNamesTests
{
    [Fact]
    public void Request_tags_are_normalized_and_deduplicated_in_order()
        => Assert.Equal(["rain", "urban", "vru"], TagNames.FromRequest([" Rain ", "urban", "RAIN", "vru"]));

    [Fact]
    public void Empty_list_removes_all_tags()
        => Assert.Empty(TagNames.FromRequest([]));

    [Fact]
    public void Missing_list_is_rejected()
        => Assert.Equal("tags", Assert.Single(Assert.Throws<RequestValidationException>(() => TagNames.FromRequest(null)).Errors.Keys));

    [Theory]
    [InlineData("")]
    [InlineData("-rain")]
    [InlineData("heavy rain")]
    [InlineData("mưa")]
    [InlineData("a234567890123456789012345678901234")]   // 34 ký tự
    public void Invalid_names_are_reported_by_index(string bad)
        => Assert.Equal("tags[1]", Assert.Single(
            Assert.Throws<RequestValidationException>(() => TagNames.FromRequest(["ok", bad])).Errors.Keys));

    [Fact]
    public void Null_entry_is_reported_by_index()
        => Assert.Equal("tags[0]", Assert.Single(
            Assert.Throws<RequestValidationException>(() => TagNames.FromRequest([null])).Errors.Keys));

    [Fact]
    public void At_most_twenty_distinct_tags()
    {
        var twenty = Enumerable.Range(0, 20).Select(i => (string?)$"t{i}").ToList();
        Assert.Equal(20, TagNames.FromRequest([.. twenty, "T0"]).Count);   // trùng sau chuẩn hoá không tính
        Assert.Equal("tags", Assert.Single(
            Assert.Throws<RequestValidationException>(() => TagNames.FromRequest([.. twenty, "t20"])).Errors.Keys));
    }

    [Fact]
    public void Content_tags_skip_invalid_values_instead_of_failing()
        => Assert.Equal(["rain", "urban"], TagNames.FromContent(JsonNode.Parse("""["Rain","bad tag",3,null,"urban","rain"]""")));

    [Fact]
    public void Content_without_tags_yields_none()
    {
        Assert.Empty(TagNames.FromContent(null));
        Assert.Empty(TagNames.FromContent(JsonNode.Parse("\"rain\"")));
    }
}
