using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Handoff;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>
/// Bàn giao Flow 1 → Flow 2 (B13): liệt kê version READY và dựng gói chạy theo scenario_version_id.
/// Chỉ đọc; không đổi trạng thái scenario/version.
/// </summary>
public interface IScenarioHandoffService
{
    /// <summary>Version READY của scenario ACTIVE (view v_ready_scenario_versions), không kèm nội dung.</summary>
    Task<PagedResult<ReadyScenarioVersionResponse>> SearchReadyAsync(ReadyVersionQueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gói chạy của một version bất kỳ (kể cả DEPRECATED hoặc scenario đã archive, để chạy lại campaign cũ);
    /// SelectableForNewCampaign cho biết có được đưa vào campaign mới hay không.
    /// </summary>
    Task<ExecutionPackageResponse> GetExecutionPackageAsync(Guid scenarioVersionId, CancellationToken ct = default);
}
