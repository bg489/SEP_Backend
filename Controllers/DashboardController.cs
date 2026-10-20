using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController(WorkflowDocumentRepository repository) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<OverviewResponse>> Overview(CancellationToken cancellationToken)
    {
        var scenarios = await repository.ListAsync<ScenarioResponse>("scenario", cancellationToken);
        var campaigns = await repository.ListAsync<CampaignResponse>("campaign", cancellationToken);
        var failures = await repository.ListAsync<FailureClusterResponse>("failure", cancellationToken);
        var runs = await repository.ListAsync<ScenarioRunResponse>("run", cancellationToken);
        var verdicts = runs.Where(run => run.Verdict is "PASS" or "FAIL" or "TIMEOUT").ToList();
        var passRate = verdicts.Count == 0 ? 0 : verdicts.Count(run => run.Verdict == "PASS") * 100d / verdicts.Count;
        var days = Enumerable.Range(0, 7).Select(offset => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offset - 6))).ToList();
        var weeklyRuns = days.Select(day => runs.Count(run => DateOnly.TryParse(run.StartedAt, out var started) && started == day)).ToList();
        var labels = days.Select(day => day.ToString("ddd")).ToList();
        var recent = new List<RecentActivityResponse>();
        recent.AddRange(runs.OrderByDescending(run => run.StartedAt).Take(3).Select(run => new RecentActivityResponse(
            run.Id, $"Scenario run {run.State.ToLowerInvariant()}", run.ScenarioName, TimeAgo(run.StartedAt),
            run.State is "Failed" or "Timed Out" ? "warning" : run.State is "Passed" ? "success" : "info")));
        recent.AddRange(scenarios.Where(scenario => scenario.Status == "Active")
            .OrderByDescending(scenario => scenario.Version).Take(2).Select(scenario => new RecentActivityResponse(
                scenario.Id, "Scenario approved", $"{scenario.Name} · {scenario.Version}", "Recently", "success")));
        return Ok(new OverviewResponse(
            scenarios.Count,
            campaigns.Count(campaign => campaign.Status is "Running" or "Queued"),
            Math.Round(passRate, 1),
            failures.Count,
            weeklyRuns,
            labels,
            recent.Take(5).ToList()));
    }

    private static string TimeAgo(string? value)
    {
        if (!DateTimeOffset.TryParse(value, out var timestamp)) return "Recently";
        var elapsed = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours} hr ago";
        return $"{(int)elapsed.TotalDays} days ago";
    }
}
