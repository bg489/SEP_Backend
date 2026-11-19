using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Oracle;

/// <summary>
/// Flow 3: Oracle chấm simulation run của Flow 2 từ Telemetry. Đọc: mọi role; nộp Telemetry: RunCampaigns.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class OracleController(IOracleService oracle) : ControllerBase
{
    [HttpGet("oracle-configs")]
    public async Task<ActionResult<IReadOnlyList<OracleConfigResponse>>> Configs(CancellationToken ct)
        => Ok(await oracle.ListConfigsAsync(ct));

    [HttpGet("runs/{id:guid}/result")]
    [ProducesResponseType<RunResultResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RunResultResponse>> Result(Guid id, CancellationToken ct)
        => Ok(await oracle.GetRunResultAsync(id, ct));

    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPost("runs/claim")]
    [ProducesResponseType<ClaimedRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<ClaimedRunResponse>> ClaimNext(
        [FromQuery] Guid? campaignId,
        CancellationToken ct,
        [FromQuery] Guid? workerId = null)
    {
        var claimed = await oracle.ClaimNextAsync(campaignId, ct, workerId);
        return claimed is null ? NoContent() : Ok(claimed);
    }

    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPost("runs/{id:guid}/running")]
    [ProducesResponseType<ClaimedRunResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClaimedRunResponse>> MarkRunning(Guid id, CancellationToken ct)
        => Ok(await oracle.MarkRunningAsync(id, ct));

    [HttpGet("runs/{id:guid}/progress")]
    public async Task<ActionResult<RunProgressResponse>> Progress(Guid id, CancellationToken ct)
        => Ok(await oracle.GetProgressAsync(id, ct));

    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPut("runs/{id:guid}/progress")]
    public async Task<ActionResult<RunProgressResponse>> Progress(Guid id, RunProgressRequest request, CancellationToken ct)
        => Ok(await oracle.SaveProgressAsync(id, request, ct));

    /// <summary>Re-queue sau INFRA_FAILED (worker auto-retry). Không dùng cho PASS/FAIL.</summary>
    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPost("runs/{id:guid}/requeue-infra")]
    [ProducesResponseType<RequeueRunResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RequeueRunResponse>> RequeueInfra(Guid id, CancellationToken ct)
        => Ok(await oracle.RequeueInfraAsync(id, ct));

    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPut("runs/{id:guid}/telemetry")]
    [Consumes("application/x-ndjson", "text/plain", "application/octet-stream")]
    [RequestSizeLimit(110 * 1024 * 1024)]
    [ProducesResponseType<RunResultResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RunResultResponse>> SubmitTelemetry(Guid id, CancellationToken ct)
        => Ok(await oracle.SubmitTelemetryAsync(id, Request.Body, ct));

    [Authorize(Policy = AuthPolicies.RunCampaigns)]
    [HttpPut("runs/{id:guid}/video")]
    [Consumes("video/mp4", "application/octet-stream")]
    [RequestSizeLimit(220 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UploadVideo(Guid id, CancellationToken ct)
    {
        await oracle.SaveVideoAsync(id, Request.Body, ct);
        return NoContent();
    }

    /// <summary>Phát lại video chase-camera của run (nếu worker đã upload).</summary>
    [HttpGet("runs/{id:guid}/video")]
    [Produces("video/mp4")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadVideo(Guid id, CancellationToken ct)
    {
        var opened = await oracle.OpenVideoAsync(id, ct);
        if (opened is null) return NotFound();
        return File(opened.Value.Stream, opened.Value.ContentType, enableRangeProcessing: true);
    }
}
