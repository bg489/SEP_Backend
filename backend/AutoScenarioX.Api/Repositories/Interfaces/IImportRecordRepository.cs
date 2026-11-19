using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IImportRecordRepository : IGenericRepository<ImportRecord>
{
    Task<ImportRecord?> GetReadOnlyAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lần import thành công đã sinh ra scenario này (nếu có).
    /// Dùng khi publish version 1 của scenario nhập từ file: version cần trỏ về import_record.
    /// </summary>
    Task<ImportRecord?> GetSucceededByScenarioAsync(Guid scenarioId, CancellationToken ct = default);

    /// <summary>Lịch sử import của một scenario, mới nhất trước.</summary>
    Task<IReadOnlyList<ImportRecord>> ListByScenarioAsync(Guid scenarioId, CancellationToken ct = default);
}
