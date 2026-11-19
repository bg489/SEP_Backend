using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IAppUserRepository : IGenericRepository<AppUser>
{
    Task<AppUser?> GetReadOnlyAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<(IReadOnlyList<AppUser> Items, int Total)> SearchAsync(string? search, string? role, bool? isActive, int page, int pageSize, CancellationToken ct = default);
    Task<int> CountActiveAdminsAsync(CancellationToken ct = default);
    Task LockAdministrationAsync(CancellationToken ct = default);
}
