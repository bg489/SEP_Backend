using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Tags.Requests;
using AutoScenarioX.Api.DTOs.Tags.Responses;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Tags;

/// <summary>Tag tổ chức repository ở mức scenario (không thuộc nội dung, không sinh version).</summary>
[Authorize(Policy = AuthPolicies.ManageScenarios)]
[ApiController]
[Produces("application/json")]
public sealed class TagsController(IScenarioTagService service) : ControllerBase
{
    /// <summary>Các tag đang được dùng, kèm số scenario (tổng và đang hoạt động).</summary>
    [HttpGet("api/v1/tags")]
    [ProducesResponseType<IReadOnlyList<TagResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TagResponse>>> List(CancellationToken ct)
        => Ok(await service.ListAsync(ct));

    /// <summary>Thay toàn bộ tag của scenario. Tên được chuẩn hoá về chữ thường; danh sách rỗng gỡ hết.</summary>
    [HttpPut("api/v1/scenarios/{scenarioId:guid}/tags")]
    [ProducesResponseType<ScenarioTagsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScenarioTagsResponse>> Replace(
        Guid scenarioId, [FromBody] ReplaceScenarioTagsRequest request, CancellationToken ct)
        => Ok(await service.ReplaceAsync(scenarioId, request, ct));
}
