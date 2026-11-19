using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Lịch sử thao tác (timeline audit) của một scenario, đọc từ asx.audit_logs.</summary>
public interface IScenarioHistoryService
{
    /// <summary>Mới nhất trước; pageSize mặc định 50, tối đa Scenario:MaxPageSize.</summary>
    Task<PagedResult<ScenarioHistoryEventResponse>> GetAsync(
        Guid scenarioId, int page, int? pageSize, CancellationToken ct = default);
}
