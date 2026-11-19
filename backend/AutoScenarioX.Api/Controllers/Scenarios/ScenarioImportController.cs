using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Scenarios;

/// <summary>Nạp scenario từ file JSON/YAML (B11).</summary>
[Authorize(Policy = AuthPolicies.ManageScenarios)]
[ApiController]
[Route("api/v1/scenarios")]
[Produces("application/json")]
public class ScenarioImportController(IScenarioImportService importService) : ControllerBase
{
    /// <summary>
    /// Nạp một file scenario thành scenario mới kèm bản nháp đã validate.
    /// Import KHÔNG tự publish - xem lại bản nháp rồi gọi POST .../versions để publish.
    /// File không hợp lệ thì trả 422 kèm report và KHÔNG tạo scenario nào.
    /// </summary>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType<ScenarioImportResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScenarioImportResponse>> Import(
        [FromForm] ImportScenarioRequest request, CancellationToken ct)
    {
        var result = await importService.ImportAsync(request, ct);
        return CreatedAtAction(
            actionName: nameof(ScenariosController.GetById),
            controllerName: "Scenarios",
            routeValues: new { scenarioId = result.Scenario.Id },
            value: result);
    }

    /// <summary>Lịch sử import của một scenario.</summary>
    [HttpGet("{scenarioId:guid}/imports")]
    [ProducesResponseType<IReadOnlyList<ImportRecordResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ImportRecordResponse>>> ListRecords(
        Guid scenarioId, CancellationToken ct)
        => Ok(await importService.ListRecordsAsync(scenarioId, ct));
}
