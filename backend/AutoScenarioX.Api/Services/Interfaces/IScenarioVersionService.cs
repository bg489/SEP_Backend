using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Nghiệp vụ trên version: lịch sử, xem nội dung, publish, deprecate, so sánh.</summary>
public interface IScenarioVersionService
{
    Task<IReadOnlyList<ScenarioVersionSummaryResponse>> ListAsync(Guid scenarioId, CancellationToken ct = default);
    Task<ScenarioVersionDetailResponse> GetByNumberAsync(Guid scenarioId, int versionNumber, CancellationToken ct = default);

    /// <summary>
    /// Chốt bản nháp thành version READY bất biến. Điều kiện: bản nháp có validation report
    /// hợp lệ và report đó tính trên đúng nội dung hiện tại (cùng content_hash).
    /// </summary>
    Task<ScenarioVersionDetailResponse> PublishAsync(Guid scenarioId, PublishVersionRequest request, CancellationToken ct = default);

    /// <summary>Đánh dấu một version là DEPRECATED (không thể quay lại READY).</summary>
    Task<ScenarioVersionSummaryResponse> DeprecateAsync(Guid scenarioId, int versionNumber, CancellationToken ct = default);

    /// <summary>So sánh nội dung hai version.</summary>
    Task<ScenarioVersionDiffResponse> DiffAsync(Guid scenarioId, int fromVersion, int toVersion, CancellationToken ct = default);
}
