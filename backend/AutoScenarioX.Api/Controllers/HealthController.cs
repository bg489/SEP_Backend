using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/health")]
[Produces("application/json")]
public class HealthController(ISystemHealthService healthService) : ControllerBase
{
    /// <summary>Kiểm tra kết nối Supabase và số bảng trong schema asx.</summary>
    [HttpGet("db")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Database(CancellationToken ct)
        => Ok(await healthService.CheckDatabaseAsync(ct));
}
