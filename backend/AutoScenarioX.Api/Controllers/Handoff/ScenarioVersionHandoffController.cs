using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Handoff;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Handoff;

/// <summary>
/// Điểm bàn giao Flow 1 → Flow 2 (B13), tra theo scenario_version_id.
/// Mọi role đã đăng nhập đều đọc được (Test Engineer chọn version cho campaign, Reviewer tra cứu).
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/scenario-versions")]
[Produces("application/json")]
public class ScenarioVersionHandoffController(IScenarioHandoffService handoffService) : ControllerBase
{
    /// <summary>
    /// Version READY của scenario ACTIVE, được phép đưa vào campaign mới. latestOnly=true chỉ lấy bản
    /// READY mới nhất của mỗi scenario. Không kèm nội dung; lấy nội dung bằng execution-package.
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType<PagedResult<ReadyScenarioVersionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReadyScenarioVersionResponse>>> SearchReady(
        [FromQuery] ReadyVersionQueryRequest request, CancellationToken ct)
        => Ok(await handoffService.SearchReadyAsync(request, ct));

    /// <summary>
    /// Gói chạy cho runner: nội dung đã thay $param bằng giá trị, spawn_point_index kèm toạ độ theo
    /// map metadata. Runnable=false kèm Issues khi không đổi được (thiếu metadata, index vượt phạm vi).
    /// Trả cả version DEPRECATED/scenario đã archive để chạy lại campaign cũ (xem SelectableForNewCampaign).
    /// </summary>
    [HttpGet("{scenarioVersionId:guid}/execution-package")]
    [ProducesResponseType<ExecutionPackageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExecutionPackageResponse>> GetExecutionPackage(Guid scenarioVersionId, CancellationToken ct)
        => Ok(await handoffService.GetExecutionPackageAsync(scenarioVersionId, ct));
}
