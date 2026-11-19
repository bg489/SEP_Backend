using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Common.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Scenarios;

/// <summary>
/// Controller MỎNG: chỉ nhận request, gọi Service, trả response.
/// Không có logic nghiệp vụ, không dùng DbContext/Repository.
/// Lỗi (404, 400...) do Service ném exception, GlobalExceptionHandler chuyển thành ProblemDetails.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/scenarios")]
[Produces("application/json")]
public class ScenariosController(IScenarioService scenarioService) : ControllerBase
{
    /// <summary>Danh sách scenario (trang Repository): tìm kiếm, lọc, phân trang.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ScenarioListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ScenarioListItemResponse>>> Search(
        [FromQuery] ScenarioQueryRequest request, CancellationToken ct)
        => Ok(await scenarioService.SearchAsync(request, ct));

    /// <summary>Chi tiết scenario + toàn bộ cấu hình của version mới nhất.</summary>
    [HttpGet("{scenarioId:guid}")]
    [ProducesResponseType<ScenarioDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioDetailResponse>> GetById(Guid scenarioId, CancellationToken ct)
        => Ok(await scenarioService.GetDetailAsync(scenarioId, ct));

    /// <summary>Tạo scenario mới kèm một bản nháp mẫu (hợp lệ ở validation tầng 1).</summary>
    [HttpPost, Authorize(Policy = AuthPolicies.ManageScenarios)]
    [ProducesResponseType<ScenarioDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScenarioDetailResponse>> Create(
        [FromBody] CreateScenarioRequest request, CancellationToken ct)
    {
        var created = await scenarioService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { scenarioId = created.Id }, created);
    }

    /// <summary>Sửa hồ sơ scenario (tên, mô tả, category). Không sinh version mới.</summary>
    [HttpPatch("{scenarioId:guid}"), Authorize(Policy = AuthPolicies.ManageScenarios)]
    [ProducesResponseType<ScenarioDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScenarioDetailResponse>> UpdateProfile(
        Guid scenarioId, [FromBody] UpdateScenarioRequest request, CancellationToken ct)
        => Ok(await scenarioService.UpdateProfileAsync(scenarioId, request, ct));

    /// <summary>Đưa scenario vào lưu trữ. Sau đó không sửa hay publish được nữa.</summary>
    [HttpPost("{scenarioId:guid}/archive"), Authorize(Policy = AuthPolicies.ManageScenarios)]
    [ProducesResponseType<ScenarioDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScenarioDetailResponse>> Archive(Guid scenarioId, CancellationToken ct)
        => Ok(await scenarioService.ArchiveAsync(scenarioId, ct));

    /// <summary>Khôi phục scenario đã lưu trữ.</summary>
    [HttpPost("{scenarioId:guid}/restore"), Authorize(Policy = AuthPolicies.ManageScenarios)]
    [ProducesResponseType<ScenarioDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScenarioDetailResponse>> Restore(Guid scenarioId, CancellationToken ct)
        => Ok(await scenarioService.RestoreAsync(scenarioId, ct));

    /// <summary>Nhân bản một version thành scenario mới kèm bản nháp chứa nội dung đã sao.</summary>
    [HttpPost("{scenarioId:guid}/clone"), Authorize(Policy = AuthPolicies.ManageScenarios)]
    [ProducesResponseType<ScenarioDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScenarioDetailResponse>> Clone(
        Guid scenarioId, [FromBody] CloneScenarioRequest request, CancellationToken ct)
    {
        var created = await scenarioService.CloneAsync(scenarioId, request, ct);
        return CreatedAtAction(nameof(GetById), new { scenarioId = created.Id }, created);
    }
}
