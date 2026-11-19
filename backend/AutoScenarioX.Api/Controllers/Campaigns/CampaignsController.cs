using AutoScenarioX.Api.DTOs.Campaigns;
using AutoScenarioX.Api.DTOs.Handoff;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Campaigns;

/// <summary>
/// Flow 2 campaigns: create/list/update, SUT config, start → simulation_runs queue.
/// Worker claim/result + oracle = Flow 3.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/campaigns")]
[Produces("application/json")]
public class CampaignsController(
    ICampaignService campaignService,
    IScenarioHandoffService handoffService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CampaignResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CampaignResponse>>> List(CancellationToken ct)
        => Ok(await campaignService.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CampaignResponse>> Get(Guid id, CancellationToken ct)
        => Ok(await campaignService.GetAsync(id, ct));

    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPost]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CampaignResponse>> Create(
        [FromBody] CreateCampaignRequest request,
        CancellationToken ct)
    {
        var created = await campaignService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CampaignResponse>> Update(
        Guid id,
        [FromBody] UpdateCampaignRequest request,
        CancellationToken ct)
        => Ok(await campaignService.UpdateAsync(id, request, ct));

    /// <summary>Queued → Running và tạo simulation_runs (mỗi version một run Queued).</summary>
    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CampaignResponse>> Start(Guid id, CancellationToken ct)
        => Ok(await campaignService.StartAsync(id, ct));

    /// <summary>Create linked new attempts; retain prior telemetry, evaluations and videos.</summary>
    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPost("{id:guid}/rerun")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CampaignResponse>> Rerun(Guid id, CancellationToken ct)
        => Ok(await campaignService.RerunAsync(id, ct));

    [HttpGet("{id:guid}/runs")]
    [ProducesResponseType<IReadOnlyList<SimulationRunResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SimulationRunResponse>>> ListRuns(
        Guid id,
        CancellationToken ct)
        => Ok(await campaignService.ListRunsAsync(id, ct));

    [HttpGet("available-scenarios")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AvailableScenarios(
        [FromQuery] ReadyVersionQueryRequest request,
        CancellationToken ct)
    {
        request.LatestOnly = true;
        return Ok(await handoffService.SearchReadyAsync(request, ct));
    }
}
