using AutoScenarioX.Api.DTOs.Tags.Requests;
using AutoScenarioX.Api.DTOs.Tags.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Tag tổ chức repository ở mức scenario: không thuộc nội dung, không sinh version.</summary>
public interface IScenarioTagService
{
    Task<IReadOnlyList<TagResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>Thay toàn bộ tag của scenario; ghi audit TAG/UNTAG cho phần thêm/gỡ.</summary>
    Task<ScenarioTagsResponse> ReplaceAsync(Guid scenarioId, ReplaceScenarioTagsRequest request, CancellationToken ct = default);
}
