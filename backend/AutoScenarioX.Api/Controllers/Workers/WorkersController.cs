using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Workers;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Workers;

[ApiController, Route("api/v1/workers"), Authorize]
public sealed class WorkersController(IWorkerManagementService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkerResponse>>> List(CancellationToken ct) => Ok(await service.ListAsync(ct));
    [HttpPost, Authorize(Policy = AuthPolicies.RunCampaigns)]
    public async Task<ActionResult<WorkerResponse>> Register(RegisterWorkerRequest request, CancellationToken ct)
        => Ok(await service.RegisterAsync(request, ct));
    [HttpPost("{id:guid}/heartbeat"), Authorize(Policy = AuthPolicies.RunCampaigns)]
    public async Task<ActionResult<WorkerResponse>> Heartbeat(Guid id, WorkerHeartbeatRequest request, CancellationToken ct)
        => Ok(await service.HeartbeatAsync(id, request, ct));
    [HttpPatch("{id:guid}"), Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<WorkerResponse>> Update(Guid id, UpdateWorkerRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
    [HttpPost("recover-lost"), Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> RecoverLost(CancellationToken ct)
    { await service.RecoverLostAsync(ct); return NoContent(); }
}
