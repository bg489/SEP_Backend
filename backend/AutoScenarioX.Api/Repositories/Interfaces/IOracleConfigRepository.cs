using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IOracleConfigRepository
{
    Task<IReadOnlyList<OracleConfig>> ListAsync(CancellationToken ct = default);
    Task<OracleConfig?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Version cao nhất, dùng làm mặc định khi tạo Campaign.</summary>
    Task<OracleConfig?> GetLatestAsync(CancellationToken ct = default);
}
