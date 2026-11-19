using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Prioritization;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

public sealed class PrioritizationServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly IPrioritizationHistoryRepository _history = Substitute.For<IPrioritizationHistoryRepository>();
    private PrioritizationService Service() => new(_uow, _history);

    [Fact]
    public async Task Duplicate_empty_and_more_than_one_hundred_ids_are_rejected_before_database_lookup()
    {
        var id = Guid.NewGuid();
        foreach (var ids in new IReadOnlyList<Guid>[] { [id, id], [Guid.Empty], Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList() })
            await Assert.ThrowsAsync<RequestValidationException>(() => Service().RankAsync(new() { ScenarioVersionIds = ids }));
        await _uow.ScenarioVersions.DidNotReceive().ListByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-.1, .5, 1)]
    [InlineData(.5, 1.1, 1)]
    [InlineData(.5, .5, 0)]
    [InlineData(.5, .5, 101)]
    public async Task Bad_weights_and_limits_are_rejected(double diversity, double risk, int limit)
        => await Assert.ThrowsAsync<RequestValidationException>(() => Service().RankAsync(new()
        { Params = new() { DiversityWeight = diversity, RiskWeight = risk, Limit = limit } }));

    [Fact]
    public async Task One_version_has_one_rank_and_reads_real_history_repository()
    {
        var version = new ScenarioVersion { Id = Guid.NewGuid(), ScenarioId = Guid.NewGuid(), Content = "{}", Status = "READY", SourceType = "manual" };
        _uow.ScenarioVersions.ListByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<ScenarioVersion> { version });
        _uow.Scenarios.GetReadOnlyAsync(version.ScenarioId, Arg.Any<CancellationToken>()).Returns(new Scenario { Id = version.ScenarioId, Name = "fixture", Code = "SCN-CI", Category = "other" });
        _history.ReadAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, VersionFailureHistory>
        { [version.Id] = new(version.Id, 5, 3, 1, ["scenario:collision"]) });
        var response = await Service().RankAsync(new() { ScenarioVersionIds = [version.Id] });
        var ranked = Assert.Single(response.Ordered);
        Assert.Equal(1, ranked.Rank);
        Assert.Equal(5, ranked.ObservedRuns);
        Assert.Equal(3, ranked.ObservedFailures);
        Assert.Equal("complete", response.Metrics.HistoryStatus);
        Assert.Equal(.5, response.Metrics.ObservedApfd);
    }

    [Fact]
    public async Task Missing_version_is_a_clear_not_found()
    {
        _uow.ScenarioVersions.ListByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ScenarioVersion>());
        await Assert.ThrowsAsync<NotFoundException>(() => Service().RankAsync(new() { ScenarioVersionIds = [Guid.NewGuid()] }));
    }
}
