using System.Text.Json;
using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class ScenarioRepository(AsxDbContext db) : GenericRepository<Scenario>(db), IScenarioRepository
{
    public async Task<PagedData<ScenarioRepositoryItem>> SearchAsync(ScenarioSearchCriteria c, CancellationToken ct = default)
    {
        var query = ApplyFilters(Db.ScenarioRepositoryView.AsNoTracking(), c);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Code)
            .Skip((c.Page - 1) * c.PageSize)
            .Take(c.PageSize)
            .ToListAsync(ct);

        return new PagedData<ScenarioRepositoryItem>(items, total);
    }

    /// <summary>Điều kiện lọc của SearchAsync, tách riêng để test câu SQL sinh ra (ToQueryString) không cần DB.</summary>
    public static IQueryable<ScenarioRepositoryItem> ApplyFilters(IQueryable<ScenarioRepositoryItem> query, ScenarioSearchCriteria c)
    {
        if (!string.IsNullOrWhiteSpace(c.Keyword))
        {
            var pattern = $"%{c.Keyword.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Code, pattern));
        }
        if (!string.IsNullOrWhiteSpace(c.Category)) query = query.Where(x => x.Category == c.Category);
        if (!string.IsNullOrWhiteSpace(c.Status))   query = query.Where(x => x.Status == c.Status);
        if (!string.IsNullOrWhiteSpace(c.MapName))  query = query.Where(x => x.MapName == c.MapName);
        if (!string.IsNullOrWhiteSpace(c.WeatherPreset)) query = query.Where(x => x.WeatherPreset == c.WeatherPreset);
        // event_types là mảng jsonb: lọc bằng @> ["<type>"]
        if (!string.IsNullOrWhiteSpace(c.EventType))
        {
            var contained = JsonSerializer.Serialize(new[] { c.EventType });
            query = query.Where(x => EF.Functions.JsonContains(x.EventTypes!, contained));
        }
        if (c.HasDraft.HasValue)                     query = query.Where(x => x.HasDraft == c.HasDraft.Value);
        foreach (var tag in c.Tags)                  query = query.Where(x => x.Tags.Contains(tag));
        return query;
    }

    public Task<Scenario?> GetReadOnlyAsync(Guid id, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
        => Set.AnyAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ScenarioRef>> ListRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => ids.Count == 0 ? [] : await Set.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new ScenarioRef(s.Id, s.Code, s.Name))
            .ToListAsync(ct);

    public async Task LockForUpdateAsync(Guid id, CancellationToken ct = default)
        => await Db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM asx.scenarios WHERE id = {id} FOR UPDATE", ct);
}
