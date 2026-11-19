using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Common.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Scenarios;

[Authorize(Policy = AuthPolicies.ManageScenarios)]
[ApiController]
[Route("api/v1/scenarios/{scenarioId:guid}/versions")]
[Produces("application/json")]
public class ScenarioVersionsController(
    IScenarioVersionService versionService,
    IScenarioExportService exportService) : ControllerBase
{
    /// <summary>Lịch sử version (mới nhất trước).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ScenarioVersionSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ScenarioVersionSummaryResponse>>> List(Guid scenarioId, CancellationToken ct)
        => Ok(await versionService.ListAsync(scenarioId, ct));

    /// <summary>
    /// Tải nội dung version về dạng file (json | yaml). Bỏ trống format thì mặc định json.
    /// File xuất ra import lại được và cho đúng content_hash cũ.
    /// </summary>
    [HttpGet("{versionNumber:int:min(1)}/export")]
    [Produces("application/json", "application/x-yaml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export(
        Guid scenarioId, int versionNumber, [FromQuery] string? format, CancellationToken ct)
    {
        var file = await exportService.ExportVersionAsync(scenarioId, versionNumber, format, ct);
        return File(System.Text.Encoding.UTF8.GetBytes(file.Content), file.MediaType, file.FileName);
    }

    /// <summary>Nội dung đầy đủ của một version.</summary>
    [HttpGet("{versionNumber:int:min(1)}")]
    [ProducesResponseType<ScenarioVersionDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioVersionDetailResponse>> GetByNumber(
        Guid scenarioId, int versionNumber, CancellationToken ct)
        => Ok(await versionService.GetByNumberAsync(scenarioId, versionNumber, ct));

    /// <summary>
    /// Publish bản nháp thành version READY bất biến. Bản nháp phải đã validate hợp lệ
    /// trên đúng nội dung hiện tại. Các version trước giữ nguyên trạng thái (vẫn READY nếu chưa bị ngừng dùng).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ScenarioVersionDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScenarioVersionDetailResponse>> Publish(
        Guid scenarioId, [FromBody] PublishVersionRequest request, CancellationToken ct)
    {
        var version = await versionService.PublishAsync(scenarioId, request, ct);
        return CreatedAtAction(nameof(GetByNumber),
            new { scenarioId, versionNumber = version.Summary.VersionNumber }, version);
    }

    /// <summary>So sánh nội dung hai version (JSON Pointer + giá trị trước/sau).</summary>
    [HttpGet("diff")]
    [ProducesResponseType<ScenarioVersionDiffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioVersionDiffResponse>> Diff(
        Guid scenarioId, [FromQuery] int from, [FromQuery] int to, CancellationToken ct)
        => Ok(await versionService.DiffAsync(scenarioId, from, to, ct));

    /// <summary>Đánh dấu version là DEPRECATED. Không thể quay lại READY.</summary>
    [HttpPost("{versionNumber:int:min(1)}/deprecate")]
    [ProducesResponseType<ScenarioVersionSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScenarioVersionSummaryResponse>> Deprecate(
        Guid scenarioId, int versionNumber, CancellationToken ct)
        => Ok(await versionService.DeprecateAsync(scenarioId, versionNumber, ct));
}
