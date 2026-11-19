using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

public class CoverageServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly CoverageService _service;
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();

    public CoverageServiceTests()
    {
        _service = new CoverageService(_uow);
        _uow.MapMetadata.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MapMetadata { MapName = "Town01" }, new MapMetadata { MapName = "Town05" }
        ]);
        ReadyScenarioVersionItem Row(Guid scenario, int number, string map, string events, bool latest) => new()
        {
            ScenarioId = scenario, ScenarioVersionId = Guid.NewGuid(), Code = "SCN", Name = "n", Category = "pedestrian",
            VersionNumber = number, MapName = map, WeatherPreset = "HardRainNoon", EventTypes = events,
            IsLatestReady = latest, ContentHash = "sha256:x"
        };
        _uow.ScenarioVersions.ListReadyAsync(true, Arg.Any<CancellationToken>()).Returns(
        [
            Row(A, 2, "Town05", """["pedestrian_crossing"]""", true),
            Row(B, 1, "Town99", "[]", true)
        ]);
        _uow.ScenarioVersions.ListReadyAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            Row(A, 1, "Town05", """["pedestrian_crossing","custom_event"]""", false),
            Row(A, 2, "Town05", """["pedestrian_crossing"]""", true)
        ]);
    }

    [Fact]
    public async Task Latest_scope_is_the_default_and_axes_include_gaps()
    {
        var result = await _service.GetAsync(null);

        Assert.Equal("latest", result.Scope);
        Assert.Equal(2, result.ScenarioCount);
        Assert.Equal(2, result.VersionCount);
        // Map chưa có scenario vẫn là một ô trống; map lạ trong dữ liệu nối cuối.
        Assert.Equal(["Town01", "Town05", "Town99"], result.Axes.Maps);
        Assert.Equal(ScenarioCatalog.WeatherPresets, result.Axes.WeatherPresets);
        Assert.Equal(ScenarioCatalog.Categories, result.Axes.Categories);
        Assert.Equal(["pedestrian_crossing"], result.Items[0].EventTypes);
        Assert.Empty(result.Items[1].EventTypes);
    }

    [Fact]
    public async Task All_scope_counts_versions_and_distinct_scenarios()
    {
        var result = await _service.GetAsync(" ALL ");

        Assert.Equal("all", result.Scope);
        Assert.Equal(1, result.ScenarioCount);
        Assert.Equal(2, result.VersionCount);
        Assert.Equal([.. ScenarioCatalog.EventTypes, "custom_event"], result.Axes.EventTypes);
    }

    [Fact]
    public async Task Unknown_scope_is_rejected()
        => Assert.Equal("scope", Assert.Single(
            (await Assert.ThrowsAsync<RequestValidationException>(() => _service.GetAsync("recent"))).Errors.Keys));
}
