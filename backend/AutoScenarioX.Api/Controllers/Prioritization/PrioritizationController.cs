using AutoScenarioX.Api.DTOs.Prioritization;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Prioritization;

/// <summary>
/// Flow ưu tiên scenario (APFD/diversity heuristic) cho Test Engineer khi dựng campaign suite.
/// Đọc lịch sử Oracle đã lưu. Tách riêng số đo quan sát và ước lượng từ nội dung.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/prioritization")]
[Produces("application/json")]
public class PrioritizationController(IPrioritizationService prioritizationService, ISimilarityService similarity) : ControllerBase
{
    /// <summary>Danh sách strategy hỗ trợ (cho FE render card chọn).</summary>
    [HttpGet("strategies")]
    [ProducesResponseType<IReadOnlyList<PrioritizationStrategyInfo>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PrioritizationStrategyInfo>> ListStrategies()
        => Ok(prioritizationService.ListStrategies());

    /// <summary>
    /// Extract feature → min-max normalize → distance matrix → rank theo strategy.
    /// Body.scenarioVersionIds trống thì lấy READY mới nhất từ kho (latestOnly).
    /// </summary>
    [HttpPost("rank")]
    [ProducesResponseType<PrioritizationRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PrioritizationRunResponse>> Rank(
        [FromBody] PrioritizeRequest request,
        CancellationToken ct)
        => Ok(await prioritizationService.RankAsync(request, ct));

    [HttpPost("compare")]
    public async Task<ActionResult<SimilarityResponse>> Compare(CompareScenariosRequest request, CancellationToken ct)
        => Ok(await similarity.CompareAsync(request, ct));

    [HttpGet("compare/{id:guid}")]
    public async Task<ActionResult<SimilarityResponse>> Comparison(Guid id, CancellationToken ct)
        => Ok(await similarity.GetAsync(id, ct));
}
