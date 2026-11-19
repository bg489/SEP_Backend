using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/analytics/failure-clusters")]
public sealed class AnalyticsController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FailureClusterResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await repository.ListAsync<FailureClusterResponse>("failure", cancellationToken));

    [HttpPost]
    public async Task<ActionResult<FailureClusterResponse>> Add(FailureClusterResponse failure, CancellationToken cancellationToken)
    {
        var ruleNames = failure.Evidence.Select(item => item.Rule).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = await repository.ListAsync<FailureClusterResponse>("failure", cancellationToken);
        var existing = all.FirstOrDefault(item => item.Id == failure.Id ||
            item.ScenarioName == failure.ScenarioName && item.Evidence.Any(evidence => ruleNames.Contains(evidence.Rule)));
        var saved = existing is null
            ? failure with { ReviewStatus = "Needs review" }
            : existing with
            {
                AffectedRuns = existing.AffectedRuns + 1,
                Evidence = [.. failure.Evidence, .. existing.Evidence],
                LastDetected = failure.LastDetected,
                SourceRunId = failure.SourceRunId ?? existing.SourceRunId,
                ReviewStatus = "Needs review"
            };
        await repository.SaveAsync("failure", saved.Id, saved, cancellationToken);
        await auditLog.WriteAsync(User, existing is null ? "Create failure cluster" : "Update failure cluster", saved.Id, "Success", cancellationToken);
        return existing is null ? CreatedAtAction(nameof(List), new { id = saved.Id }, saved) : Ok(saved);
    }

    [HttpPost("{id}/confirm")]
    [Authorize(Roles = RoleNames.TestReviewer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<FailureClusterResponse>> Confirm(string id, [FromBody] JsonObject changes, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync<FailureClusterResponse>("failure", id, cancellationToken);
        if (current is null) return NotFound(new MessageResponse("Failure cluster not found."));
        var originalLabel = current.OriginalLabel;
        if (changes.TryGetPropertyValue("label", out var labelNode) && labelNode is JsonValue labelValue &&
            labelValue.TryGetValue<string>(out var label) && !string.Equals(label, current.Label, StringComparison.Ordinal))
            originalLabel ??= current.Label;
        var json = JsonSerializer.SerializeToNode(current, JsonOptions)!.AsObject();
        foreach (var change in changes)
        {
            if (change.Key is "id" or "reviewStatus" or "evidence" or "affectedRuns") continue;
            json[change.Key] = change.Value?.DeepClone();
        }
        var updated = json.Deserialize<FailureClusterResponse>(JsonOptions)! with
        {
            Id = id,
            OriginalLabel = originalLabel,
            ReviewStatus = "Confirmed",
            ReviewedBy = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "Reviewer"
        };
        await repository.SaveAsync("failure", id, updated, cancellationToken);
        await auditLog.WriteAsync(User, changes.ContainsKey("label") ? "Correct failure classification" : "Confirm failure classification", id, "Success", cancellationToken);
        return Ok(updated);
    }
}
