using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Sửa hồ sơ ghi audit UPDATE_PROFILE chỉ với các trường thật sự đổi (migration 106).</summary>
public class ScenarioServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ScenarioService _service;
    private readonly Scenario _scenario = new()
    {
        Id = Guid.NewGuid(), Code = "SCN-09997", Name = "Old name", Description = "Old description",
        Category = "pedestrian", Status = ScenarioStatuses.Active, OriginType = ScenarioOriginTypes.Manual
    };
    private readonly List<object?> _audits = [];

    public ScenarioServiceTests()
    {
        _service = new ScenarioService(_uow, FakeUnitOfWork.User(), Options.Create(new ScenarioOptions()));
        _uow.Scenarios.FindAsync(Arg.Is<object[]>(k => (Guid)k[0] == _scenario.Id), Arg.Any<CancellationToken>()).Returns(_scenario);
        _uow.Tags.ListNamesAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns([]);
        _uow.AuditLogs.Write(AuditEntityTypes.Scenario, _scenario.Id, AuditActions.UpdateProfile, Arg.Any<Guid>(),
            Arg.Do<object?>(details => _audits.Add(details)));
    }

    private Task Patch(string? name = null, string? description = null, string? category = null)
        => _service.UpdateProfileAsync(_scenario.Id, new UpdateScenarioRequest { Name = name, Description = description, Category = category });

    private string AuditJson() => JsonSerializer.Serialize(Assert.Single(_audits));

    [Fact]
    public async Task Only_changed_fields_are_recorded()
    {
        await Patch(name: "  New name ", category: "cut_in", description: "Old description");

        Assert.Equal("""{"changes":{"name":{"from":"Old name","to":"New name"},"category":{"from":"pedestrian","to":"cut_in"}}}""", AuditJson());
        Assert.Equal(("New name", "cut_in"), (_scenario.Name, _scenario.Category));
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Sending_the_same_values_writes_no_audit()
    {
        await Patch(name: "Old name", category: "pedestrian", description: "Old description");
        Assert.Empty(_audits);
    }

    [Fact]
    public async Task Clearing_the_description_records_null()
    {
        await Patch(description: "   ");

        Assert.Equal("""{"changes":{"description":{"from":"Old description","to":null}}}""", AuditJson());
        Assert.Null(_scenario.Description);
    }

    [Fact]
    public async Task Omitted_fields_stay_unchanged()
    {
        await Patch(name: "Renamed");

        Assert.Equal("""{"changes":{"name":{"from":"Old name","to":"Renamed"}}}""", AuditJson());
        Assert.Equal("Old description", _scenario.Description);
    }

    [Fact]
    public async Task Archived_scenario_cannot_be_edited()
    {
        _scenario.Status = ScenarioStatuses.Archived;
        await Assert.ThrowsAsync<BusinessRuleException>(() => Patch(name: "New name"));
        Assert.Empty(_audits);
        Assert.Equal(0, _uow.SaveCount);
    }
}

/// <summary>GET /scenarios: weather và eventType phải thuộc catalog rồi mới xuống repository.</summary>
public class ScenarioSearchServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ScenarioService _service;

    public ScenarioSearchServiceTests()
    {
        _service = new ScenarioService(_uow, FakeUnitOfWork.User(), Options.Create(new ScenarioOptions()));
        _uow.Scenarios.SearchAsync(Arg.Any<ScenarioSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedData<ScenarioRepositoryItem>([], 0));
    }

    [Fact]
    public async Task Valid_filters_reach_the_repository()
    {
        await _service.SearchAsync(new ScenarioQueryRequest { Weather = "HardRainNoon", EventType = "pedestrian_crossing" });

        await _uow.Scenarios.Received(1).SearchAsync(
            Arg.Is<ScenarioSearchCriteria>(c => c.WeatherPreset == "HardRainNoon" && c.EventType == "pedestrian_crossing"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("hardrainnoon", null, "weather")]
    [InlineData(null, "collision", "eventType")]
    public async Task Unknown_values_are_rejected(string? weather, string? eventType, string field)
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => _service.SearchAsync(new ScenarioQueryRequest { Weather = weather, EventType = eventType }));
        Assert.True(ex.Errors.ContainsKey(field));
    }
}
