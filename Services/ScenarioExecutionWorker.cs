using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;

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
            var campaign = await repository.GetAsync<CampaignResponse>("campaign", run.CampaignId, cancellationToken);
            var timeout = Math.Clamp(campaign?.TimeoutSeconds ?? 300, 1, 86400);
            var result = await carlaRuntime.RunScenarioAsync(integration, run.Id, artifact.FileName, artifact.Content, timeout, runCancellation.Token);
            var error = result.TimedOut ? $"ScenarioRunner exceeded the campaign timeout of {timeout} seconds." :
                result.ExitCode != 0 ? $"ScenarioRunner exited with code {result.ExitCode?.ToString() ?? "unknown"}." : null;
            var state = result.TimedOut ? "Timed Out" : result.ExitCode == 0 ? "Passed" : "Failed";
            var verdict = result.TimedOut ? "TIMEOUT" : result.ExitCode == 0 ? "PASS" : "FAIL";
            var evidence = error is null ? [] : new List<FailureEvidenceResponse>
            {
                new(DateTimeOffset.UtcNow.ToString("O"), "ScenarioRunner execution", "Process result", "PASS", error)
            };
            await callbackWriter.ApplyAsync(run.Id, new WorkerRunCallbackRequest(
                state, 100, verdict, workerId, LimitLog(result.Log), error, evidence, result.DurationSeconds), cancellationToken);
            logger.LogInformation("Run {RunId} finished as {State} ({Verdict}).", run.Id, state, verdict);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            logger.LogInformation("Stopped ScenarioRunner process for cancelled run {RunId}.", run.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Run {RunId} could not be executed.", run.Id);
            await callbackWriter.ApplyAsync(run.Id, new WorkerRunCallbackRequest(
                "Failed", 100, "FAIL", workerId, Error: exception.Message, Log: exception.ToString()), CancellationToken.None);
        }
        finally
        {
            _activeRuns.TryRemove(run.Id, out _);
        }
    }

    private static string LimitLog(string log)
    {
        const int maxCharacters = 500_000;
        return log.Length <= maxCharacters ? log : $"[Log truncated to the last {maxCharacters} characters]\n" + log[^maxCharacters..];
    }
}
