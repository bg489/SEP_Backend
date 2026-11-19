using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Oracle;

[ApiController]
[Authorize(Policy = AuthPolicies.AdminOnly)]
[Route("api/v1/admin")]
public sealed class AdminOracleController(IAdminOracleService admin) : ControllerBase
{
    [HttpGet("oracle-configs")]
    public async Task<ActionResult<IReadOnlyList<AdminOracleConfigResponse>>> Configs(CancellationToken ct)
        => Ok(await admin.ListAsync(ct));

    [HttpPost("oracle-configs")]
    public async Task<ActionResult<AdminOracleConfigResponse>> Create(CreateOracleConfigRequest request, CancellationToken ct)
        => Ok(await admin.CreateAsync(request, ct));

    [HttpPost("oracle-configs/{id:guid}/activate")]
    public async Task<ActionResult<SystemSettingsResponse>> Activate(Guid id, CancellationToken ct)
        => Ok(await admin.ActivateAsync(id, ct));

    [HttpGet("settings")]
    public async Task<ActionResult<SystemSettingsResponse>> Settings(CancellationToken ct) => Ok(await admin.SettingsAsync(ct));

    [HttpPut("settings")]
    public async Task<ActionResult<SystemSettingsResponse>> UpdateSettings(UpdateSystemSettingsRequest request, CancellationToken ct)
        => Ok(await admin.UpdateSettingsAsync(request, ct));

    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<AuditEntryResponse>>> Audit([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await admin.AuditAsync(page, pageSize, ct));
}
