using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IScenarioVersionRepository : IGenericRepository<ScenarioVersion>
{
    Task<IReadOnlyList<ScenarioVersion>> ListByScenarioAsync(Guid scenarioId, CancellationToken ct = default);
    Task<ScenarioVersion?> GetByNumberAsync(Guid scenarioId, int versionNumber, CancellationToken ct = default);
    Task<ScenarioVersion?> GetReadOnlyAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>Tải nhiều version (kèm Content) theo danh sách id, AsNoTracking.</summary>
    Task<IReadOnlyList<ScenarioVersion>> ListByIdsAsync(IEnumerable<Guid> versionIds, CancellationToken ct = default);

    /// <summary>Version của scenario có cùng content_hash (unique theo uq_versions_hash), không có thì null.</summary>
    Task<ScenarioVersion?> FindByContentHashAsync(Guid scenarioId, string contentHash, CancellationToken ct = default);

    /// <summary>Tìm trong view bàn giao v_ready_scenario_versions (READY + scenario ACTIVE).</summary>
    Task<PagedData<ReadyScenarioVersionItem>> SearchReadyAsync(ReadyVersionSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Toàn bộ dòng của view bàn giao (không phân trang) cho thống kê coverage, sắp theo mã rồi version.</summary>
    Task<IReadOnlyList<ReadyScenarioVersionItem>> ListReadyAsync(bool latestOnly, CancellationToken ct = default);

    /// <summary>Số version và scenario của các version theo ID, không tải cột content.</summary>
    Task<IReadOnlyList<VersionRef>> ListRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}

public sealed record VersionRef(Guid Id, Guid ScenarioId, int VersionNumber);
