using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IScenarioRepository : IGenericRepository<Scenario>
{
    Task<PagedData<ScenarioRepositoryItem>> SearchAsync(ScenarioSearchCriteria criteria, CancellationToken ct = default);
    Task<Scenario?> GetReadOnlyAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Khoá dòng scenario (SELECT ... FOR UPDATE). Chỉ gọi bên trong transaction.</summary>
    Task LockForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Mã và tên của các scenario theo ID (kể cả đã lưu trữ).</summary>
    Task<IReadOnlyList<ScenarioRef>> ListRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}

public sealed record ScenarioRef(Guid Id, string Code, string Name);
