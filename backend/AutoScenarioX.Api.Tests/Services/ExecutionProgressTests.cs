using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

public sealed class ExecutionProgressTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly SimulationRun _run = new() { Id = Guid.NewGuid(), Status = SimulationRunStatuses.Running };
    private OracleService Service()
    {
        _uow.Runs.GetForUpdateAsync(_run.Id, Arg.Any<CancellationToken>()).Returns(_run);
        _uow.Runs.GetResultAsync(_run.Id, Arg.Any<CancellationToken>()).Returns(_run);
        return new(_uow, Options.Create(new TelemetryOptions()));
    }

    [Fact]
    public async Task Progress_is_persisted_and_readable_during_execution()
    {
        var service = Service();
        var request = new RunProgressRequest(2.5, 50, 1, 2, 0.5, 20, "Running Town01");
        await service.SaveProgressAsync(_run.Id, request);
        var result = await service.GetProgressAsync(_run.Id);
        Assert.Equal(request, result.Progress);
        Assert.NotNull(result.UpdatedAt);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Late_progress_cannot_overwrite_a_newer_frame()
    {
        var service = Service();
        await service.SaveProgressAsync(_run.Id, new(3, 60, 1, 2, 0.5, 20));
        await Assert.ThrowsAsync<ConflictException>(() => service.SaveProgressAsync(_run.Id, new(2, 40, 0, 0, 0, 0)));
        Assert.Equal(60, (await service.GetProgressAsync(_run.Id)).Progress!.Frame);
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(0, -1)]
    public async Task Invalid_or_nonfinite_progress_is_rejected(double x, long frame)
    {
        var service = Service();
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveProgressAsync(_run.Id, new(0, frame, x, 0, 0, 0)));
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Finished_or_historical_run_rejects_live_progress()
    {
        var service = Service();
        _run.IsCurrent = false;
        await Assert.ThrowsAsync<ConflictException>(() => service.SaveProgressAsync(_run.Id, new(0, 0, 0, 0, 0, 0)));
        _run.IsCurrent = true;
        _run.Status = SimulationRunStatuses.Completed;
        await Assert.ThrowsAsync<ConflictException>(() => service.SaveProgressAsync(_run.Id, new(0, 0, 0, 0, 0, 0)));
    }
}
