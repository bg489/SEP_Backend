using System.Text;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Nộp Telemetry của simulation run (Flow 2): Oracle chấm, lưu Evaluation, ghi bản sao verdict.</summary>
public class OracleServiceTests : IDisposable
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "asx-telemetry-" + Guid.NewGuid());
    private readonly OracleService _service;
    private readonly SimulationRun _run;
    private readonly OracleConfig _config = new() { Id = Guid.NewGuid(), Version = 1, Name = "v1" };
    private Evaluation? _saved;

    public OracleServiceTests()
    {
        _service = new OracleService(_uow, Options.Create(new TelemetryOptions { Directory = _dir, MaxUploadMb = 1 }));
        var version = new ScenarioVersion { Id = Guid.NewGuid(), ScenarioId = Guid.NewGuid(), VersionNumber = 1 };
        var campaignId = Guid.NewGuid();
        _run = new SimulationRun
        {
            Id = Guid.NewGuid(), CampaignId = campaignId, ScenarioVersionId = version.Id, Status = "Running",
            Campaign = new Campaign
            {
                Id = campaignId, Name = "C", Code = "CMP-00001", OracleConfigId = _config.Id,
                SutType = "behavior_agent", SutBehavior = "normal", SutCarlaVersion = "0.9.16",
                Runs = new List<SimulationRun>(),
            },
        };
        _run.Campaign.Runs.Add(_run);
        _uow.Runs.GetForUpdateAsync(_run.Id, Arg.Any<CancellationToken>()).Returns(_run);
        _uow.Runs.GetResultAsync(_run.Id, Arg.Any<CancellationToken>()).Returns(_run);
        _uow.Runs.When(r => r.AddEvaluation(Arg.Any<Evaluation>())).Do(c =>
        {
            _saved = c.Arg<Evaluation>();
            _saved.OracleConfig = _config;
            _run.Evaluations.Add(_saved);
        });
        _uow.OracleConfigs.GetAsync(_config.Id, Arg.Any<CancellationToken>()).Returns(_config);
        _uow.ScenarioVersions.GetReadOnlyAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _uow.Scenarios.GetReadOnlyAsync(version.ScenarioId, Arg.Any<CancellationToken>())
            .Returns(new Scenario { Id = version.ScenarioId, Code = "SCN-00001", Name = "S" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private Stream Telemetry(string fixture, string? runId = null) => new MemoryStream(Encoding.UTF8.GetBytes(
        File.ReadAllText(TestData.TelemetryPath(fixture))
            .Replace("00000000-0000-0000-0000-0000000000f1", runId ?? _run.Id.ToString())));

    [Fact]
    public async Task Speeding_run_is_judged_stored_and_verdict_copied_to_the_run()
    {
        var result = await _service.SubmitTelemetryAsync(_run.Id, Telemetry("speeding.jsonl"));

        Assert.Equal("FAIL", result.Verdict);
        Assert.Equal("FAIL", _run.Verdict);
        Assert.Equal("speeding", Assert.Single(_saved!.Violations).CheckName);
        _uow.Runs.Received(1).AddTelemetry(Arg.Is<RunTelemetry>(t => t.Outcome == "COMPLETED" && t.DurationS == 6.5m));
        Assert.True(File.Exists(Path.Combine(_dir, _run.CampaignId.ToString(), $"{_run.Id}.jsonl.gz")));
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Telemetry_of_another_run_is_rejected_without_saving()
    {
        var e = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _service.SubmitTelemetryAsync(_run.Id, Telemetry("speeding.jsonl", Guid.NewGuid().ToString())));

        Assert.Equal(ErrorCodes.TelemetryRunMismatch, e.Code);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Second_upload_and_unclaimed_run_are_conflicts()
    {
        _run.Telemetry = new RunTelemetry();
        var again = await Assert.ThrowsAsync<ConflictException>(() => _service.SubmitTelemetryAsync(_run.Id, Telemetry("speeding.jsonl")));
        Assert.Equal(ErrorCodes.TelemetryAlreadyReceived, again.Code);

        _run.Telemetry = null;
        _run.Status = SimulationRunStatuses.Queued;
        var queued = await Assert.ThrowsAsync<ConflictException>(() => _service.SubmitTelemetryAsync(_run.Id, Telemetry("speeding.jsonl")));
        Assert.Equal(ErrorCodes.RunNotStarted, queued.Code);
    }

    [Fact]
    public async Task Unreadable_telemetry_is_stored_as_error_evaluation()
    {
        var result = await _service.SubmitTelemetryAsync(_run.Id, new MemoryStream("not json\n{}"u8.ToArray()));

        Assert.Equal("ERROR", result.Verdict);
        Assert.NotNull(_saved!.ErrorReason);
        _uow.Runs.Received(1).AddTelemetry(Arg.Is<RunTelemetry>(t => t.Outcome == null));
    }
}
