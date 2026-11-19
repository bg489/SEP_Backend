using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public sealed class WorkerRepository(AsxDbContext db) : IWorkerRepository
{
    public async Task<IReadOnlyList<SimulationWorker>> ListAsync(CancellationToken ct = default)
        => await db.Set<SimulationWorker>().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
    public Task<SimulationWorker?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Set<SimulationWorker>().FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task AddAsync(SimulationWorker worker, CancellationToken ct = default)
        => await db.Set<SimulationWorker>().AddAsync(worker, ct);
    public Task<int> ActiveRunsAsync(Guid id, CancellationToken ct = default)
        => db.SimulationRuns.CountAsync(r => r.WorkerId == id && r.IsCurrent &&
            (r.Status == SimulationRunStatuses.Claimed || r.Status == SimulationRunStatuses.Running), ct);
    public async Task SaveAsync(CancellationToken ct = default) => await db.SaveChangesAsync(ct);
    public Task<int> RecoverLostAsync(CancellationToken ct = default)
        => db.Database.ExecuteSqlRawAsync("SELECT asx.recover_lost_workers()", ct);
}
