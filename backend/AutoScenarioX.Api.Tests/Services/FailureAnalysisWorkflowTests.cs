using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.FailureAnalysis;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

public sealed class FailureAnalysisWorkflowTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly Campaign _campaign = new() { Id = Guid.NewGuid(), Name = "fixture", Code = "CMP-CI" };
    private readonly AnalysisSession _session;
    private readonly FailureAnalysisService _service;

    public FailureAnalysisWorkflowTests()
    {
        _session = new() { Id = Guid.NewGuid(), CampaignId = _campaign.Id, Campaign = _campaign, Status = AnalysisSteps.ReportDraft };
        _uow.Campaigns.GetReadOnlyAsync(_campaign.Id, Arg.Any<CancellationToken>()).Returns(_campaign);
        _uow.FailureAnalyses.GetByCampaignAsync(_campaign.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_session);
        _uow.Runs.ListForAnalysisAsync(_campaign.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<SimulationRun>());
        _service = new(_uow, Options.Create(new TelemetryOptions()));
    }

    [Fact]
    public async Task Invalid_review_decision_does_not_save()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => _service.ReviewAsync(_campaign.Id, new("approve-everything", null)));
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Review_before_report_is_refused()
    {
        _session.Status = AnalysisSteps.Grouped;
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.ReviewAsync(_campaign.Id, new("complete", null)));
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Needs_more_is_persisted_without_claiming_effectiveness_is_ready()
    {
        var response = await _service.ReviewAsync(_campaign.Id, new("needs_more", " need video "));
        Assert.Equal(AnalysisSteps.NeedsMore, response.Step);
        Assert.Equal("need video", _session.ReviewNote);
        Assert.Null(_session.Effectiveness);
        Assert.Equal(1, _uow.SaveCount);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.GetEvaluationAsync(_campaign.Id));
    }

    [Fact]
    public async Task Complete_persists_a_readable_result_but_no_faults_means_no_apfd()
    {
        var response = await _service.ReviewAsync(_campaign.Id, new("complete", "reviewed"));
        Assert.Equal(AnalysisSteps.Evaluated, response.Step);
        Assert.NotNull(_session.Effectiveness);
        var evaluation = await _service.GetEvaluationAsync(_campaign.Id);
        Assert.Null(evaluation.Proposed.Apfd);
        Assert.Null(evaluation.Proposed.Apfdc);
    }

    [Fact]
    public async Task Return_to_flow3_creates_a_linked_attempt_without_removing_old_artifacts()
    {
        _session.Status = AnalysisSteps.Unexplained;
        var old = new SimulationRun { Id = Guid.NewGuid(), CampaignId = _campaign.Id, Campaign = _campaign,
            Status = "Completed", Verdict = "FAIL", IsCurrent = true,
            Telemetry = new() { TelemetryPath = "keep.jsonl.gz", VideoPath = "keep.mp4" } };
        _session.Faults.Add(new AnalysisFault { Id = Guid.NewGuid(), Status = AnalysisSteps.Unexplained,
            Runs = new List<AnalysisFaultRun> { new() { RunId = old.Id } } });
        _uow.Runs.GetForUpdateAsync(old.Id, Arg.Any<CancellationToken>()).Returns(old);
        _uow.Runs.AddRetryAsync(old, Arg.Any<CancellationToken>()).Returns(new SimulationRun { Id = Guid.NewGuid(), PreviousRunId = old.Id, Status = "Queued" });
        var response = await _service.ReturnToFlow3Async(_campaign.Id);
        Assert.Equal(AnalysisSteps.AwaitingFlow3, response.Step);
        Assert.Equal("keep.mp4", old.Telemetry.VideoPath);
        Assert.Equal("FAIL", old.Verdict);
        await _uow.Runs.Received(1).AddRetryAsync(old, Arg.Any<CancellationToken>());
        _uow.Runs.DidNotReceive().RemoveInfraArtifacts(Arg.Any<SimulationRun>());
    }
}
