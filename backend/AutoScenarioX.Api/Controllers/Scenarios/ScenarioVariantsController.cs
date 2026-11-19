using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Scenarios;

[Authorize(Policy = AuthPolicies.ManageScenarios)]
[ApiController]
[Route("api/v1/scenarios/{scenarioId:guid}/versions/{versionNumber:int:min(1)}/variant-batches")]
[Produces("application/json")]
public sealed class ScenarioVariantsController(IScenarioVariantService service) : ControllerBase
{
    /// <summary>Sinh tổ hợp của parameterGrid (strategy cartesian | pairwise) thành scenario + draft đã validate. Không tự publish.</summary>
    [HttpPost]
    [ProducesResponseType<VariantBatchResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VariantBatchResponse>> Generate(
        Guid scenarioId, int versionNumber, [FromBody] GenerateVariantsRequest request, CancellationToken ct)
    {
        var result = await service.GenerateAsync(scenarioId, versionNumber, request, ct);
        return CreatedAtAction(nameof(Get), new { scenarioId, versionNumber, batchId = result.Batch.Id }, result);
    }

    /// <summary>Lịch sử sinh biến thể từ version gốc.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VariantBatchSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<VariantBatchSummaryResponse>>> List(
        Guid scenarioId, int versionNumber, CancellationToken ct)
        => Ok(await service.ListAsync(scenarioId, versionNumber, ct));

    /// <summary>Grid gốc và danh sách scenario của batch, gồm trạng thái draft/publish hiện tại.</summary>
    [HttpGet("{batchId:guid}")]
    [ProducesResponseType<VariantBatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VariantBatchResponse>> Get(
        Guid scenarioId, int versionNumber, Guid batchId, CancellationToken ct)
        => Ok(await service.GetAsync(scenarioId, versionNumber, batchId, ct));
}
