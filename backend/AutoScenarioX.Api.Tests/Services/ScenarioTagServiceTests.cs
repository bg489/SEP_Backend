using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Tags.Requests;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Thay bộ tag: chỉ gắn/gỡ phần chênh lệch, audit TAG/UNTAG, chặn scenario đã lưu trữ.</summary>
public class ScenarioTagServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ScenarioTagService _service;
    private readonly Scenario _scenario = new()
    {
        Id = Guid.NewGuid(), Code = "SCN-09998", Name = "Tags", Category = "pedestrian",
        Status = ScenarioStatuses.Active, OriginType = ScenarioOriginTypes.Manual
    };

    public ScenarioTagServiceTests()
    {
        _service = new ScenarioTagService(_uow, FakeUnitOfWork.User());
        _uow.Scenarios.GetReadOnlyAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(_scenario);
        _uow.Tags.ListNamesAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(["rain", "urban"]);
    }

    private Task<DTOs.Tags.Responses.ScenarioTagsResponse> Replace(params string?[] tags)
        => _service.ReplaceAsync(_scenario.Id, new ReplaceScenarioTagsRequest { Tags = [.. tags] });

    private static bool Same(IReadOnlyCollection<string> actual, params string[] expected)
        => actual.SequenceEqual(expected);

    [Fact]
    public async Task Only_the_difference_is_attached_and_detached_with_audit()
    {
        var result = await Replace("Night", "rain", "vru");

        Assert.Equal(["night", "rain", "vru"], result.Tags);
        await _uow.Tags.Received(1).AttachAsync(_scenario.Id, Arg.Is<IReadOnlyCollection<string>>(t => Same(t, "night", "vru")), Arg.Any<CancellationToken>());
        await _uow.Tags.Received(1).DetachAsync(_scenario.Id, Arg.Is<IReadOnlyCollection<string>>(t => Same(t, "urban")), Arg.Any<CancellationToken>());
        _uow.AuditLogs.Received(1).Write(AuditEntityTypes.Scenario, _scenario.Id, AuditActions.Tag, Arg.Any<Guid>(), Arg.Any<object?>());
        _uow.AuditLogs.Received(1).Write(AuditEntityTypes.Scenario, _scenario.Id, AuditActions.Untag, Arg.Any<Guid>(), Arg.Any<object?>());
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Same_set_writes_nothing()
    {
        var result = await Replace("urban", "RAIN");

        Assert.Equal(["rain", "urban"], result.Tags);
        Assert.Equal(0, _uow.SaveCount);
        _uow.AuditLogs.DidNotReceiveWithAnyArgs().Write(default!, default, default!, default, default);
    }

    [Fact]
    public async Task Empty_list_removes_every_tag()
    {
        var result = await Replace();

        Assert.Empty(result.Tags);
        await _uow.Tags.Received(1).DetachAsync(_scenario.Id, Arg.Is<IReadOnlyCollection<string>>(t => Same(t, "rain", "urban")), Arg.Any<CancellationToken>());
        _uow.AuditLogs.DidNotReceive().Write(Arg.Any<string>(), Arg.Any<Guid>(), AuditActions.Tag, Arg.Any<Guid>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task Archived_scenario_is_rejected()
    {
        _scenario.Status = ScenarioStatuses.Archived;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Replace("rain"));
        Assert.Equal(ErrorCodes.ScenarioArchived, error.Code);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Unknown_scenario_is_not_found()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ReplaceAsync(Guid.NewGuid(), new ReplaceScenarioTagsRequest { Tags = ["rain"] }));
        Assert.Equal(ErrorCodes.ScenarioNotFound, error.Code);
    }

    [Fact]
    public async Task Invalid_tag_is_rejected_before_touching_the_database()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => Replace("ok", "bad tag"));
        await _uow.Scenarios.DidNotReceive().LockForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
