using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Scenarios;

[Authorize(Policy = AuthPolicies.ManageScenarios)]
[ApiController]
[Route("api/v1/scenarios/{scenarioId:guid}/history")]
[Produces("application/json")]
public sealed class ScenarioHistoryController(IScenarioHistoryService service) : ControllerBase
{
    /// <summary>
    /// Lịch sử thao tác của scenario, mới nhất trước: tạo/clone/import, draft (tạo, lưu, validate, huỷ, export),
    /// publish/deprecate/export version, sinh biến thể, tag, archive/restore. pageSize mặc định 50.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ScenarioHistoryEventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ScenarioHistoryEventResponse>>> Get(
        Guid scenarioId, [FromQuery] int page = 1, [FromQuery] int? pageSize = null, CancellationToken ct = default)
        => Ok(await service.GetAsync(scenarioId, page, pageSize, ct));
}
