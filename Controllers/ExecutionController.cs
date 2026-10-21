using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using AutoScenarioX.AuthApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class ExecutionController(
    WorkflowDocumentRepository repository,
    AuditLogWriter auditLog,
    CarlaRuntime carlaRuntime,
    ScenarioExecutionWorker executionWorker) : ControllerBase
{
    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<ScenarioRunResponse>>> ListRuns(CancellationToken cancellationToken) =>
        Ok(await repository.ListAsync<ScenarioRunResponse>("run", cancellationToken));

    [HttpPost("campaigns/{id}/runs")]
    [Authorize(Roles = RoleNames.TestEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<IReadOnlyList<ScenarioRunResponse>>> QueueRuns(
        string id,
        QueueCampaignRunsRequest request,
        CancellationToken cancellationToken)
    {
        var campaign = await repository.GetAsync<CampaignResponse>("campaign", id, cancellationToken);
        if (campaign is null) return NotFound(new MessageResponse("Campaign not found."));
        var scenarioIds = request.ScenarioIds?.Distinct(StringComparer.Ordinal).ToList() ?? campaign.ScenarioIds;
        if (scenarioIds.Count == 0) return BadRequest(new MessageResponse("The campaign has no scenarios to execute."));
        var scenarios = await repository.ListAsync<ScenarioResponse>("scenario", cancellationToken);
        var selected = scenarioIds.Select(scenarioId => scenarios.FirstOrDefault(item => item.Id == scenarioId)).ToList();
        if (selected.Any(item => item is null)) return BadRequest(new MessageResponse("One or more requested scenarios do not exist."));
        if (selected.Any(item => item!.Status is not ("Active" or "Approved")))
            return BadRequest(new MessageResponse("Only Active or Approved scenarios can be executed."));
        var integration = await repository.GetAsync<CarlaIntegrationResponse>("integration", "carla", cancellationToken);
        if (integration is not { Active: true, ConnectionStatus: "Ready" or "Connected" })
            return Conflict(new MessageResponse("Activate a ready CARLA 0.9.16 and ScenarioRunner installation before queueing real runs."));
        var readiness = await carlaRuntime.CheckReadinessAsync(integration, cancellationToken);
        if (!readiness.Ready) return Conflict(new MessageResponse(readiness.Message));
        foreach (var scenario in selected)
        {
            var artifact = await repository.GetAsync<ScenarioArtifactResponse>("scenario-artifact", $"{scenario!.Id}:{scenario.Version}", cancellationToken);
            if (artifact is null)
                return Conflict(new MessageResponse($"Scenario '{scenario.Name}' has no saved .xosc/.osc source for version {scenario.Version}. Re-import the source file before executing it."));
        }

        var repetitionCount = Math.Clamp(campaign.Repetitions ?? 1, 1, 100);
        var createdAt = DateTimeOffset.UtcNow;
        var newRuns = selected.SelectMany(scenario => Enumerable.Range(1, repetitionCount).Select(repetition =>
            new ScenarioRunResponse(
                NewId("RUN"), campaign.Id, scenario!.Id, scenario.Name, scenario.Version, "Queued", 0, null,
                null, null, null, request.RetryOf, repetition, campaign.SutVersion ?? "1.0.0",
                campaign.Agent ?? "Behavior Agent", (campaign.RandomSeed ?? 42) + repetition - 1,
                campaign.OracleProfile ?? "Default safety profile", [], [])))
            .ToList();
        foreach (var run in newRuns)
            await repository.SaveAsync("run", run.Id, run, cancellationToken);
        var updatedCampaign = campaign with { Status = "Queued", Progress = 0, FailedRuns = 0, PassRate = 0 };
        await repository.SaveAsync("campaign", campaign.Id, updatedCampaign, cancellationToken);
        await auditLog.WriteAsync(User, "Queue campaign execution", campaign.Id, "Success", cancellationToken);
        return Accepted(newRuns);
    }

    [HttpGet("runs/{id}/artifacts/{fileName}")]
    public async Task<IActionResult> GetArtifact(string id, string fileName, CancellationToken cancellationToken)
    {
        var run = await repository.GetAsync<ScenarioRunResponse>("run", id, cancellationToken);
        if (run is null) return NotFound(new MessageResponse("Run not found."));
        if (!run.Artifacts.Contains(fileName, StringComparer.Ordinal)) return NotFound(new MessageResponse("Artifact not found."));
        var artifact = await repository.GetAsync<RunArtifactResponse>("run-artifact", $"{id}:{fileName}", cancellationToken);
        if (artifact is null) return NotFound(new MessageResponse("Artifact not found."));
        return File(System.Text.Encoding.UTF8.GetBytes(artifact.Content), artifact.ContentType, artifact.FileName);
    }

    [HttpPost("campaigns/{id}/runs/cancel")]
    [Authorize(Roles = RoleNames.TestEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<IReadOnlyList<ScenarioRunResponse>>> CancelRuns(string id, CancellationToken cancellationToken)
    {
        var campaign = await repository.GetAsync<CampaignResponse>("campaign", id, cancellationToken);
        if (campaign is null) return NotFound(new MessageResponse("Campaign not found."));
        var runs = await repository.ListAsync<ScenarioRunResponse>("run", cancellationToken);
        var active = runs.Where(run => run.CampaignId == id && run.State is "Queued" or "Assigned" or "Running").ToList();
        if (active.Count == 0) return Ok(Array.Empty<ScenarioRunResponse>());
        foreach (var run in active)
        {
            executionWorker.CancelRun(run.Id);
            await repository.SaveAsync("run", run.Id, run with { State = "Cancelled", Progress = 0 }, cancellationToken);
        }
        await repository.SaveAsync("campaign", id, campaign with { Status = "Cancelled", Progress = 0 }, cancellationToken);
        await auditLog.WriteAsync(User, "Cancel campaign execution", id, "Info", cancellationToken);
        return Ok(active.Select(run => run with { State = "Cancelled", Progress = 0 }));
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}
