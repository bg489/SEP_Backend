using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Common.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AutoScenarioX.Api.Controllers.Scenarios;

[Authorize(Policy = AuthPolicies.ManageScenarios)]
[ApiController]
[Route("api/v1/scenarios/{scenarioId:guid}/draft")]
[Produces("application/json")]
public class ScenarioDraftsController(
    IScenarioDraftService draftService,
    IScenarioValidationService validationService,
    IScenarioExportService exportService) : ControllerBase
{
    /// <summary>Bản nháp hiện tại. Có header ETag = rowVersion (dùng cho If-Match khi lưu).</summary>
    [HttpGet]
    [ProducesResponseType<ScenarioDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioDraftResponse>> Get(Guid scenarioId, CancellationToken ct)
        => WithETag(await draftService.GetAsync(scenarioId, ct));

    /// <summary>
    /// Tạo bản nháp (tối đa 1 / scenario). Nội dung lấy từ version fromVersion nếu truyền,
    /// không thì từ version mới nhất; chưa có version thì dùng mẫu.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ScenarioDraftResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScenarioDraftResponse>> Create(
        Guid scenarioId, [FromQuery] int? fromVersion, CancellationToken ct)
    {
        var draft = await draftService.CreateAsync(scenarioId, fromVersion, ct);
        SetETag(draft);
        return CreatedAtAction(nameof(Get), new { scenarioId }, draft);
    }

    /// <summary>
    /// Lưu nội dung bản nháp. BẮT BUỘC gửi header If-Match = ETag lấy từ GET;
    /// nếu người khác đã lưu trước đó thì trả 409.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<ScenarioDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScenarioDraftResponse>> Save(
        Guid scenarioId, [FromBody] SaveDraftRequest request, CancellationToken ct)
        => WithETag(await draftService.SaveAsync(scenarioId, request, ReadIfMatch(), ct));

    /// <summary>Tải bản nháp đang soạn về dạng file (json | yaml).</summary>
    [HttpGet("export")]
    [Produces("application/json", "application/x-yaml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(Guid scenarioId, [FromQuery] string? format, CancellationToken ct)
    {
        var file = await exportService.ExportDraftAsync(scenarioId, format, ct);
        return File(System.Text.Encoding.UTF8.GetBytes(file.Content), file.MediaType, file.FileName);
    }

    /// <summary>Huỷ bản nháp (bỏ mọi thay đổi chưa publish).</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid scenarioId, CancellationToken ct)
    {
        await draftService.DeleteAsync(scenarioId, ct);
        return NoContent();
    }

    /// <summary>
    /// Chạy validate 3 tầng (schema -> business -> carla) trên bản nháp,
    /// lưu report và gắn vào bản nháp. Đây là điều kiện để publish version.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType<ValidationReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ValidationReportResponse>> Validate(Guid scenarioId, CancellationToken ct)
        => Ok(await validationService.ValidateDraftAsync(scenarioId, ct));

    private void SetETag(ScenarioDraftResponse draft)
        => Response.Headers.ETag = $"\"{draft.RowVersion}\"";

    private ActionResult<ScenarioDraftResponse> WithETag(ScenarioDraftResponse draft)
    {
        SetETag(draft);
        return Ok(draft);
    }

    /// <summary>Đọc header If-Match ("3" hoặc W/"3") thành rowVersion.</summary>
    private int ReadIfMatch()
    {
        var raw = Request.Headers[HeaderNames.IfMatch].ToString();
        var value = raw.Trim().TrimStart('W', '/').Trim('"');

        if (!int.TryParse(value, out var rowVersion))
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["If-Match"] = ["Missing or invalid If-Match header. Retrieve the draft to get its ETag, then try again."]
            });

        return rowVersion;
    }
}
