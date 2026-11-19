using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public sealed class VariantBatchRepository(AsxDbContext db)
    : GenericRepository<VariantBatch>(db), IVariantBatchRepository
{
    public async Task<IReadOnlyList<VariantBatch>> ListByVersionAsync(Guid baseVersionId, CancellationToken ct = default)
        => await Set.AsNoTracking().Where(b => b.BaseVersionId == baseVersionId)
            .OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id).ToListAsync(ct);

    public Task<VariantBatch?> GetReadOnlyAsync(Guid id, Guid baseVersionId, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && b.BaseVersionId == baseVersionId, ct);

    public async Task<IReadOnlyList<Scenario>> ListScenariosAsync(Guid batchId, CancellationToken ct = default)
        => await Db.Scenarios.AsNoTracking().Where(s => s.VariantBatchId == batchId)
            .Include(s => s.Draft).OrderBy(s => s.Name).ThenBy(s => s.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<VariantBatch>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => ids.Count == 0 ? [] : await Set.AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync(ct);
}
