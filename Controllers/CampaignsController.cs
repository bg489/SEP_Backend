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
[Route("api/campaigns")]
public sealed class CampaignsController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Statuses = ["Running", "Queued", "Completed", "Draft", "Needs review", "Failed", "Cancelled"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CampaignResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await repository.ListAsync<CampaignResponse>("campaign", cancellationToken));

    [HttpPost]
    [Authorize(Roles = RoleNames.TestEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<CampaignResponse>> Create(CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.ScenarioIds.Count == 0)
            return BadRequest(new MessageResponse("Campaign name and at least one scenario are required."));
        var scenarios = await repository.ListAsync<ScenarioResponse>("scenario", cancellationToken);
        var selected = request.ScenarioIds.Distinct(StringComparer.Ordinal).Select(id => scenarios.FirstOrDefault(item => item.Id == id)).ToList();
        if (selected.Any(item => item is null)) return BadRequest(new MessageResponse("One or more selected scenarios do not exist."));
        if (selected.Any(item => item!.Status is not ("Active" or "Approved")))
            return BadRequest(new MessageResponse("Only Active or Approved scenarios can be added to a campaign."));
        if (request.Repetitions is < 1 or > 100 || request.TimeoutSeconds is < 1 or > 86400)
            return BadRequest(new MessageResponse("Repetitions or timeout is outside its supported range."));
        var campaign = new CampaignResponse(
            NewId("CMP"), request.Name.Trim(), request.Description?.Trim() ?? string.Empty,
            request.ScenarioIds.Distinct(StringComparer.Ordinal).ToList(), request.Strategy.Trim(), "Draft", 0,
            User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "AutoScenarioX user",
            DateTimeOffset.UtcNow.ToString("O"), 0, 0, "00:00:00", request.SutName, request.SutVersion,
            request.Agent ?? "Behavior Agent", request.RandomSeed ?? 42, request.Repetitions ?? 1,
            request.TimeoutSeconds ?? 300, request.OracleProfile ?? "Default safety profile");
        await repository.SaveAsync("campaign", campaign.Id, campaign, cancellationToken);
        await auditLog.WriteAsync(User, "Create campaign", campaign.Id, "Success", cancellationToken);
        return CreatedAtAction(nameof(List), new { id = campaign.Id }, campaign);
    }

    [HttpPatch("{id}")]
    [Authorize(Roles = RoleNames.TestEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<CampaignResponse>> Update(string id, [FromBody] JsonObject changes, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync<CampaignResponse>("campaign", id, cancellationToken);
        if (current is null) return NotFound(new MessageResponse("Campaign not found."));
        if (changes.TryGetPropertyValue("status", out var status) && status is not null && !Statuses.Contains(status.GetValue<string>()))
            return BadRequest(new MessageResponse("Unsupported campaign status."));
        var json = JsonSerializer.SerializeToNode(current, JsonOptions)!.AsObject();
        foreach (var change in changes)
        {
            if (change.Key == "id" || change.Key == "owner" || change.Key == "createdAt") continue;
            json[change.Key] = change.Value?.DeepClone();
        }
        var updated = json.Deserialize<CampaignResponse>(JsonOptions)! with { Id = id, Owner = current.Owner, CreatedAt = current.CreatedAt };
        await repository.SaveAsync("campaign", id, updated, cancellationToken);
        await auditLog.WriteAsync(User, "Update campaign", id, "Success", cancellationToken);
        return Ok(updated);
    }

    [HttpGet("{id}/ranking")]
    public async Task<IActionResult> GetRanking(string id, CancellationToken cancellationToken)
    {
        if (await repository.GetAsync<CampaignResponse>("campaign", id, cancellationToken) is null)
            return NotFound(new MessageResponse("Campaign not found."));
        var ranking = await repository.GetAsync<JsonObject>("campaign-ranking", id, cancellationToken);
        return ranking is null ? NoContent() : Ok(ranking);
    }

    [HttpPut("{id}/ranking")]
    [Authorize(Roles = RoleNames.TestEngineer + "," + RoleNames.Administrator)]
    public async Task<IActionResult> SaveRanking(string id, [FromBody] JsonObject ranking, CancellationToken cancellationToken)
    {
        if (await repository.GetAsync<CampaignResponse>("campaign", id, cancellationToken) is null)
            return NotFound(new MessageResponse("Campaign not found."));
        await repository.SaveAsync("campaign-ranking", id, ranking, cancellationToken);
        await auditLog.WriteAsync(User, "Save campaign prioritization evaluation", id, "Success", cancellationToken);
        return NoContent();
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}
