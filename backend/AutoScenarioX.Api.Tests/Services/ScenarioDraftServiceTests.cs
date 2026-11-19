using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Lưu bản nháp với ETag/If-Match (row_version là concurrency token).</summary>
public class ScenarioDraftServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ScenarioDraftService _service;
    private readonly Scenario _scenario = new()
    {
        Id = Guid.NewGuid(), Code = "SCN-09998", Name = "Draft", Category = "pedestrian",
        Status = ScenarioStatuses.Active, OriginType = ScenarioOriginTypes.Manual
    };
    private readonly ScenarioDraft _draft;

    public ScenarioDraftServiceTests()
    {
        _service = new ScenarioDraftService(_uow, FakeUnitOfWork.User(), Options.Create(new ScenarioOptions { MaxContentSizeKb = 1 }));
        _draft = new ScenarioDraft { ScenarioId = _scenario.Id, Content = "{}", RowVersion = 5, LastValidationReportId = Guid.NewGuid() };
        _uow.Scenarios.GetReadOnlyAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(_scenario);
        _uow.ScenarioDrafts.GetForUpdateAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(_draft);
    }

    private static SaveDraftRequest Request(string json) => new() { Content = JsonDocument.Parse(json).RootElement.Clone() };

    [Fact]
    public async Task Save_with_matching_revision_bumps_row_version_and_invalidates_report()
    {
        var result = await _service.SaveAsync(_scenario.Id, Request("""{"map":{"name":"Town05"}}"""), ifMatchRowVersion: 5);

        Assert.Equal(6, result.RowVersion);
        Assert.Null(_draft.LastValidationReportId);
        Assert.Equal("""{"map":{"name":"Town05"}}""", _draft.Content);
        _uow.AuditLogs.Received(1).Write(AuditEntityTypes.Draft, _scenario.Id, AuditActions.UpdateDraft, Arg.Any<Guid>(), Arg.Any<object?>());
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Stale_revision_is_conflict_and_keeps_draft()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.SaveAsync(_scenario.Id, Request("""{"a":1}"""), ifMatchRowVersion: 4));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, error.Code);
        Assert.Equal(5, _draft.RowVersion);
        Assert.Equal("{}", _draft.Content);
        Assert.NotNull(_draft.LastValidationReportId);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Archived_scenario_cannot_be_edited()
    {
        _scenario.Status = ScenarioStatuses.Archived;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => _service.SaveAsync(_scenario.Id, Request("{}"), 5));
        Assert.Equal(ErrorCodes.ScenarioArchived, error.Code);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    public async Task Content_must_be_an_object(string json)
    {
        var error = await Assert.ThrowsAsync<RequestValidationException>(() => _service.SaveAsync(_scenario.Id, Request(json), 5));
        Assert.True(error.Errors.ContainsKey("content"));
        Assert.Equal(5, _draft.RowVersion);
    }

    [Fact]
    public async Task Content_over_size_limit_is_rejected()
    {
        var large = "{\"x\":\"" + new string('a', 1100) + "\"}";   // giới hạn test: 1 KB
        var error = await Assert.ThrowsAsync<RequestValidationException>(() => _service.SaveAsync(_scenario.Id, Request(large), 5));
        Assert.True(error.Errors.ContainsKey("content"));
    }

    [Fact]
    public async Task Missing_draft_is_not_found()
    {
        _uow.ScenarioDrafts.GetForUpdateAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns((ScenarioDraft?)null);
        var error = await Assert.ThrowsAsync<NotFoundException>(() => _service.SaveAsync(_scenario.Id, Request("{}"), 5));
        Assert.Equal(ErrorCodes.DraftNotFound, error.Code);
    }
}
