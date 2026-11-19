using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class AppUserRepository(AsxDbContext db)
    : GenericRepository<AppUser>(db), IAppUserRepository
{
    public Task<AppUser?> GetReadOnlyAsync(Guid id, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<AppUser>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => ids.Count == 0 ? [] : await Set.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => Set.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct);

    public Task<int> CountActiveAdminsAsync(CancellationToken ct = default)
        => Set.CountAsync(u => u.IsActive && u.Role == "ADMIN", ct);

    public async Task<(IReadOnlyList<AppUser> Items, int Total)> SearchAsync(string? search, string? role, bool? isActive, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(u => u.Role == role);
        if (isActive.HasValue) query = query.Where(u => u.IsActive == isActive);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(u => u.Email).ThenBy(u => u.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, count);
    }
}
