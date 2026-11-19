using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class ScenarioVersionRepository(AsxDbContext db)
    : GenericRepository<ScenarioVersion>(db), IScenarioVersionRepository
{
    public async Task<IReadOnlyList<ScenarioVersion>> ListByScenarioAsync(Guid scenarioId, CancellationToken ct = default)
        => await Set.AsNoTracking()
            .Where(v => v.ScenarioId == scenarioId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

    public Task<ScenarioVersion?> GetByNumberAsync(Guid scenarioId, int versionNumber, CancellationToken ct = default)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(v => v.ScenarioId == scenarioId && v.VersionNumber == versionNumber, ct);

    public Task<ScenarioVersion?> GetReadOnlyAsync(Guid versionId, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);

    public async Task<IReadOnlyList<ScenarioVersion>> ListByIdsAsync(IEnumerable<Guid> versionIds, CancellationToken ct = default)
    {
        var ids = versionIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await Set.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToListAsync(ct);
    }

    public Task<ScenarioVersion?> FindByContentHashAsync(Guid scenarioId, string contentHash, CancellationToken ct = default)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(v => v.ScenarioId == scenarioId && v.ContentHash == contentHash, ct);

    public async Task<PagedData<ReadyScenarioVersionItem>> SearchReadyAsync(ReadyVersionSearchCriteria c, CancellationToken ct = default)
    {
        var query = Db.ReadyScenarioVersionView.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(c.Keyword))
        {
            var pattern = $"%{c.Keyword.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Code, pattern));
        }
        if (!string.IsNullOrWhiteSpace(c.Category)) query = query.Where(x => x.Category == c.Category);
        if (!string.IsNullOrWhiteSpace(c.MapName))  query = query.Where(x => x.MapName == c.MapName);
        if (c.ScenarioId is { } scenarioId)          query = query.Where(x => x.ScenarioId == scenarioId);
        if (c.LatestOnly)                            query = query.Where(x => x.IsLatestReady);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Code)
            .ThenByDescending(x => x.VersionNumber)
            .Skip((c.Page - 1) * c.PageSize)
            .Take(c.PageSize)
            .ToListAsync(ct);

        return new PagedData<ReadyScenarioVersionItem>(items, total);
    }

    public async Task<IReadOnlyList<VersionRef>> ListRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => ids.Count == 0 ? [] : await Set.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new VersionRef(v.Id, v.ScenarioId, v.VersionNumber))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReadyScenarioVersionItem>> ListReadyAsync(bool latestOnly, CancellationToken ct = default)
    {
        var query = Db.ReadyScenarioVersionView.AsNoTracking();
        if (latestOnly) query = query.Where(x => x.IsLatestReady);
        return await query.OrderBy(x => x.Code).ThenBy(x => x.VersionNumber).ToListAsync(ct);
    }
}
