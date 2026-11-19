using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.FailureAnalysis;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.FailureAnalysis;

/// <summary>
/// Flow 4 đi từng cổng: gom, verify, lỗi giống, giải thích, report, rồi mới đánh giá APFD.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/campaigns/{campaignId:guid}/failure-analysis")]
[Produces("application/json")]
public sealed class FailureAnalysisController(IFailureAnalysisService analysis) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FailureAnalysisResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FailureAnalysisResponse>> Get(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.GetAsync(campaignId, ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ReviewResults)]
    [ProducesResponseType<FailureAnalysisResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FailureAnalysisResponse>> Group(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.GroupAsync(campaignId, ct));

    [HttpPost("verify")]
    [Authorize(Policy = AuthPolicies.ReviewResults)]
    public async Task<ActionResult<FailureAnalysisResponse>> Verify(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.VerifyAsync(campaignId, ct));

    [HttpPost("similar")]
    [Authorize(Policy = AuthPolicies.ReviewResults)]
    public async Task<ActionResult<FailureAnalysisResponse>> Similar(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.FindSimilarAsync(campaignId, ct));

    [HttpPost("explainability")]
    [Authorize(Policy = AuthPolicies.ReviewResults)]
    public async Task<ActionResult<FailureAnalysisResponse>> Explainability(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.CheckExplainabilityAsync(campaignId, ct));

    /// <summary>Người dùng chủ động đưa các fault chưa giải thích được về hàng chờ Flow 3.</summary>
    [HttpPost("return-to-flow3")]
    [Authorize(Policy = AuthPolicies.ReviewResults)]
    public async Task<ActionResult<FailureAnalysisResponse>> ReturnToFlow3(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.ReturnToFlow3Async(campaignId, ct));

    [HttpPost("generate")]
    [Authorize(Policy = AuthPolicies.ReviewResults)]
    public async Task<ActionResult<FailureAnalysisResponse>> Generate(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.GenerateAsync(campaignId, ct));

    [HttpPost("review")]
    [Authorize(Policy = AuthPolicies.ReviewResults)]
    public async Task<ActionResult<FailureAnalysisResponse>> Review(
        Guid campaignId,
        [FromBody] ReviewFailureReportRequest request,
        CancellationToken ct)
        => Ok(await analysis.ReviewAsync(campaignId, request, ct));

    [HttpGet("evaluation")]
    [ProducesResponseType<EffectivenessResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EffectivenessResponse>> Evaluation(Guid campaignId, CancellationToken ct)
        => Ok(await analysis.GetEvaluationAsync(campaignId, ct));
}
