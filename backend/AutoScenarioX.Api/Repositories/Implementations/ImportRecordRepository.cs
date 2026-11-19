using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class ImportRecordRepository(AsxDbContext db)
    : GenericRepository<ImportRecord>(db), IImportRecordRepository
{
    public Task<ImportRecord?> GetReadOnlyAsync(Guid id, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<ImportRecord?> GetSucceededByScenarioAsync(Guid scenarioId, CancellationToken ct = default)
        => Set.AsNoTracking()
            .Where(r => r.ScenarioId == scenarioId && r.Status == ImportStatuses.Succeeded)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ImportRecord>> ListByScenarioAsync(Guid scenarioId, CancellationToken ct = default)
        => await Set.AsNoTracking()
            .Where(r => r.ScenarioId == scenarioId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
}
