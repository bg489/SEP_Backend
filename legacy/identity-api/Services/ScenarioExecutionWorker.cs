using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using System.Text.Json;

namespace AutoScenarioX.AuthApi.Services;

public sealed class ScenarioExecutionWorker(
    IServiceScopeFactory scopeFactory,
    CarlaRuntime carlaRuntime,
    ILogger<ScenarioExecutionWorker> logger) : BackgroundService
{
    private readonly string _workerId = $"LOCAL-{Environment.MachineName}";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _activeRuns = new(StringComparer.Ordinal);

    public void CancelRun(string runId)
    {
        if (_activeRuns.TryGetValue(runId, out var cancellation)) cancellation.Cancel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Local CARLA ScenarioRunner worker started as {WorkerId}; execution quality is locked to Low.", _workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextRunAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "CARLA worker loop failed; it will retry shortly.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        logger.LogInformation("Local CARLA ScenarioRunner worker stopped.");
    }

    private async Task ProcessNextRunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<WorkflowDocumentRepository>();
        var callbackWriter = scope.ServiceProvider.GetRequiredService<ScenarioRunCallbackWriter>();
        var integration = await repository.GetAsync<CarlaIntegrationResponse>("integration", "carla", cancellationToken);
        if (integration is not { Active: true, ConnectionStatus: "Ready" or "Connected" }) return;
        var runs = await repository.ListAsync<ScenarioRunResponse>("run", cancellationToken);
        var run = runs.FirstOrDefault(item => item.State == "Queued");
        if (run is null) return;

        var workerId = _workerId;
        var scenario = await repository.GetAsync<ScenarioResponse>("scenario", run.ScenarioId, cancellationToken);
        var artifact = await repository.GetAsync<ScenarioArtifactResponse>("scenario-artifact", $"{run.ScenarioId}:{run.ScenarioVersion}", cancellationToken);
        if (scenario is null || artifact is null)
        {
            await callbackWriter.ApplyAsync(run.Id, new WorkerRunCallbackRequest(
                "Failed", 100, "FAIL", workerId, Error: $"Saved OpenSCENARIO source for {run.ScenarioId} version {run.ScenarioVersion} is missing."), cancellationToken);
            return;
        }

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeRuns[run.Id] = runCancellation;
        try
        {
            logger.LogInformation("Claimed run {RunId} for scenario {ScenarioId}.", run.Id, run.ScenarioId);
            await callbackWriter.ApplyAsync(run.Id, new WorkerRunCallbackRequest("Assigned", 1, WorkerId: workerId), cancellationToken);
            await callbackWriter.ApplyAsync(run.Id, new WorkerRunCallbackRequest("Running", 5, WorkerId: workerId), cancellationToken);
            // Execute the saved version, even if the engineer edits the scenario after queueing it.
            var executionScenario = scenario;
            if (artifact.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                executionScenario = JsonSerializer.Deserialize<ScenarioResponse>(artifact.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("The saved scenario version is not a valid CARLA actor configuration.");
                if (executionScenario.Id != run.ScenarioId || executionScenario.Version != run.ScenarioVersion)
                    throw new InvalidOperationException("The saved scenario configuration does not match the queued version.");
            }
            var campaign = await repository.GetAsync<CampaignResponse>("campaign", run.CampaignId, cancellationToken);
            var timeout = Math.Clamp(campaign?.TimeoutSeconds ?? 300, 1, 86400);
            var oracleRules = await repository.ListAsync<OracleRuleResponse>("oracle-rule", cancellationToken);
            if (oracleRules.Count == 0) oracleRules = OracleRuleCatalog.Defaults;
            var optionsArtifact = await repository.GetAsync<RunArtifactResponse>("run-artifact", $"{run.Id}:execution-options.json", cancellationToken);
            if (optionsArtifact is not null)
            {
                var options = JsonSerializer.Deserialize<ExecutionOptionsSnapshotResponse>(optionsArtifact.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("The saved execution options are invalid.");
                timeout = options.TimeoutSeconds;
                oracleRules = options.OracleRules;
            }
            var result = artifact.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? await carlaRuntime.RunConfiguredScenarioAsync(integration, run.Id, executionScenario, run, oracleRules, timeout, runCancellation.Token)
                : await carlaRuntime.RunScenarioAsync(integration, run.Id, artifact.FileName, artifact.Content, timeout, runCancellation.Token);
            runCancellation.Token.ThrowIfCancellationRequested();
            var error = result.TimedOut
                ? $"CARLA execution exceeded the campaign timeout of {timeout} seconds."
                : result.Passed == true
                    ? null
                    : result.Error ?? (result.ExitCode != 0
                        ? $"CARLA execution exited with code {result.ExitCode?.ToString() ?? "unknown"}."
                        : "CARLA execution did not report a successful scenario outcome; it was not marked PASS.");
            var state = result.TimedOut ? "Timed Out" : result.Passed == true ? "Passed" : "Failed";
            var verdict = result.TimedOut ? "TIMEOUT" : result.Passed == true ? "PASS" : "FAIL";
            var evidence = result.Evidence ?? (error is null ? [] : new List<FailureEvidenceResponse>
            {
                new(DateTimeOffset.UtcNow.ToString("O"), "ScenarioRunner execution", "Process result", "PASS", error)
            });
            if (result.ResultJson is not null)
            {
                await repository.SaveAsync("run-artifact", $"{run.Id}:simulation-result.json",
                    new RunArtifactResponse(run.Id, "simulation-result.json", "application/json", result.ResultJson, DateTimeOffset.UtcNow.ToString("O")), cancellationToken);
                var latest = await repository.GetAsync<ScenarioRunResponse>("run", run.Id, cancellationToken);
                if (latest is not null && latest.State != "Cancelled")
                    await repository.SaveAsync("run", run.Id, latest with { Artifacts = [.. latest.Artifacts, "simulation-result.json"] }, cancellationToken);
            }
            runCancellation.Token.ThrowIfCancellationRequested();
            var beforeCompletion = await repository.GetAsync<ScenarioRunResponse>("run", run.Id, runCancellation.Token);
            if (beforeCompletion is null || IsTerminal(beforeCompletion.State))
            {
                logger.LogInformation("Skipped completion callback for run {RunId}; its current state is {State}.", run.Id, beforeCompletion?.State ?? "missing");
                return;
            }
            await callbackWriter.ApplyAsync(run.Id, new WorkerRunCallbackRequest(
                state, 100, verdict, workerId, LimitLog(result.Log), error, evidence, result.DurationSeconds), runCancellation.Token);
            logger.LogInformation("Run {RunId} finished as {State} ({Verdict}).", run.Id, state, verdict);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            logger.LogInformation("Stopped ScenarioRunner process for cancelled run {RunId}.", run.Id);
        }
        catch (Exception exception)
        {
            var latest = await repository.GetAsync<ScenarioRunResponse>("run", run.Id, CancellationToken.None);
            if (runCancellation.IsCancellationRequested || latest is null || IsTerminal(latest.State))
            {
                logger.LogInformation("Skipped error callback for run {RunId}; it was cancelled or is already {State}.", run.Id, latest?.State ?? "missing");
                return;
            }
            logger.LogError(exception, "Run {RunId} could not be executed.", run.Id);
            await callbackWriter.ApplyAsync(run.Id, new WorkerRunCallbackRequest(
                "Failed", 100, "FAIL", workerId, Error: exception.Message, Log: exception.ToString()), CancellationToken.None);
        }
        finally
        {
            _activeRuns.TryRemove(run.Id, out _);
        }
    }

    private static bool IsTerminal(string state) => state is "Passed" or "Failed" or "Timed Out" or "Cancelled";

    private static string LimitLog(string log)
    {
        const int maxCharacters = 500_000;
        return log.Length <= maxCharacters ? log : $"[Log truncated to the last {maxCharacters} characters]\n" + log[^maxCharacters..];
    }
}
