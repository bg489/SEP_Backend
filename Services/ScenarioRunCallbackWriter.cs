using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;

namespace AutoScenarioX.AuthApi.Services;

public sealed class ScenarioRunCallbackWriter(WorkflowDocumentRepository repository)
{
    private static readonly HashSet<string> States = ["Assigned", "Running", "Passed", "Failed", "Timed Out"];

    public async Task<ScenarioRunResponse?> ApplyAsync(string runId, WorkerRunCallbackRequest callback, CancellationToken cancellationToken)
    {
        if (!States.Contains(callback.State)) throw new ArgumentException("Unsupported worker state.");
        if (callback.Progress is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(callback.Progress));
        var current = await repository.GetAsync<ScenarioRunResponse>("run", runId, cancellationToken);
        if (current is null) return null;
        if (current.State is "Passed" or "Failed" or "Timed Out" or "Cancelled")
            throw new InvalidOperationException("The run is already terminal and cannot accept more callbacks.");
        var allowed = current.State switch
        {
            "Queued" => callback.State is "Assigned" or "Failed" or "Timed Out",
            "Assigned" => callback.State is "Running" or "Failed" or "Timed Out",
            "Running" => callback.State is "Running" or "Passed" or "Failed" or "Timed Out",
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"Run state cannot transition from {current.State} to {callback.State}.");

        var terminal = callback.State is "Passed" or "Failed" or "Timed Out";
        var verdict = callback.Verdict ?? callback.State switch
        {
            "Passed" => "PASS",
            "Failed" => "FAIL",
            "Timed Out" => "TIMEOUT",
            _ => null
        };
        if (terminal && verdict is not ("PASS" or "FAIL" or "TIMEOUT"))
            throw new ArgumentException("Terminal callbacks require a PASS, FAIL or TIMEOUT verdict.");
        var evidence = callback.Evidence ?? current.Evidence;
        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            evidence = [.. evidence, new FailureEvidenceResponse(
                DateTimeOffset.UtcNow.ToString("O"), "ScenarioRunner execution", "Process result", "PASS",
                callback.Error.Length > 1000 ? callback.Error[..1000] : callback.Error)];
        }
        var artifacts = current.Artifacts;
        if (!string.IsNullOrWhiteSpace(callback.Log) && !artifacts.Contains("scenario-run.log", StringComparer.Ordinal))
            artifacts = [.. artifacts, "scenario-run.log"];
        var startedAt = callback.State == "Running" ? current.StartedAt ?? DateTimeOffset.UtcNow.ToString("O") : current.StartedAt;
        var updated = current with
        {
            State = callback.State,
            Progress = terminal ? 100 : Math.Max(current.Progress, callback.Progress),
            Verdict = verdict,
            StartedAt = startedAt,
            DurationSeconds = callback.DurationSeconds ?? current.DurationSeconds,
            WorkerId = callback.WorkerId ?? current.WorkerId,
            Artifacts = artifacts,
            Evidence = evidence
        };
        await repository.SaveAsync("run", runId, updated, cancellationToken);
        if (!string.IsNullOrWhiteSpace(callback.Log))
        {
            await repository.SaveAsync("run-artifact", $"{runId}:scenario-run.log",
                new RunArtifactResponse(runId, "scenario-run.log", "text/plain; charset=utf-8", callback.Log, DateTimeOffset.UtcNow.ToString("O")),
                cancellationToken);
        }
        await UpdateCampaignAsync(updated, cancellationToken);
        return updated;
    }

    private async Task UpdateCampaignAsync(ScenarioRunResponse changedRun, CancellationToken cancellationToken)
    {
        var campaign = await repository.GetAsync<CampaignResponse>("campaign", changedRun.CampaignId, cancellationToken);
        if (campaign is null) return;
        var runs = await repository.ListAsync<ScenarioRunResponse>("run", cancellationToken);
        var campaignRuns = runs.Where(run => run.CampaignId == changedRun.CampaignId).ToList();
        if (campaignRuns.Count == 0) return;
        var finished = campaignRuns.Count(run => run.State is "Passed" or "Failed" or "Timed Out" or "Cancelled");
        var passed = campaignRuns.Count(run => run.State == "Passed");
        var failed = campaignRuns.Count(run => run.State is "Failed" or "Timed Out");
        var hasActive = campaignRuns.Any(run => run.State is "Queued" or "Assigned" or "Running");
        var status = hasActive ? "Running" : campaignRuns.All(run => run.State == "Cancelled") ? "Cancelled" : failed > 0 ? "Failed" : "Completed";
        await repository.SaveAsync("campaign", campaign.Id, campaign with
        {
            Status = status,
            Progress = (int)Math.Round(finished * 100d / campaignRuns.Count),
            PassRate = finished == 0 ? 0 : Math.Round(passed * 100d / finished, 1),
            FailedRuns = failed
        }, cancellationToken);
    }
}
