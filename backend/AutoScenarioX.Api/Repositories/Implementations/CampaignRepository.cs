using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class CampaignRepository(AsxDbContext db) : ICampaignRepository
{
    public async Task<IReadOnlyList<Campaign>> ListAsync(CancellationToken ct = default)
        => await db.Campaigns.AsNoTracking()
            .Include(c => c.Creator)
            .Include(c => c.Versions)
            .ThenInclude(v => v.ScenarioVersion)
            .ThenInclude(v => v.Scenario)
            .Include(c => c.Runs)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task<Campaign?> GetReadOnlyAsync(Guid id, CancellationToken ct = default)
        => db.Campaigns.AsNoTracking()
            .Include(c => c.Creator)
            .Include(c => c.Versions)
            .ThenInclude(v => v.ScenarioVersion)
            .ThenInclude(v => v.Scenario)
            .Include(c => c.Runs)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Campaign?> GetTrackedAsync(Guid id, CancellationToken ct = default)
        => db.Campaigns
            .Include(c => c.Versions)
            .Include(c => c.Runs)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(Campaign campaign, CancellationToken ct = default)
        => await db.Campaigns.AddAsync(campaign, ct);

    public async Task AddRunsAsync(IEnumerable<SimulationRun> runs, CancellationToken ct = default)
        => await db.SimulationRuns.AddRangeAsync(runs, ct);

    public async Task<IReadOnlyList<SimulationRun>> ListRunsAsync(Guid campaignId, CancellationToken ct = default)
        => await db.SimulationRuns.AsNoTracking()
            .Include(r => r.ScenarioVersion)
            .ThenInclude(v => v.Scenario)
            .Where(r => r.CampaignId == campaignId)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);

    public Task<bool> HasRunsAsync(Guid campaignId, CancellationToken ct = default)
        => db.SimulationRuns.AsNoTracking().AnyAsync(r => r.CampaignId == campaignId, ct);

    public async Task ClearRunsAsync(Guid campaignId, CancellationToken ct = default)
    {
        var ids = await db.SimulationRuns
            .Where(r => r.CampaignId == campaignId)
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (ids.Count == 0) return;

        var evalIds = await db.Evaluations
            .Where(e => ids.Contains(e.RunId))
            .Select(e => e.Id)
            .ToListAsync(ct);
        if (evalIds.Count > 0)
            await db.Violations.Where(v => evalIds.Contains(v.EvaluationId)).ExecuteDeleteAsync(ct);
        await db.Evaluations.Where(e => ids.Contains(e.RunId)).ExecuteDeleteAsync(ct);
        await db.RunTelemetry.Where(t => ids.Contains(t.RunId)).ExecuteDeleteAsync(ct);
        await db.SimulationRuns.Where(r => r.CampaignId == campaignId).ExecuteDeleteAsync(ct);
        foreach (var entry in db.ChangeTracker.Entries<SimulationRun>()
                     .Where(e => e.Entity.CampaignId == campaignId)
                     .ToList())
            entry.State = EntityState.Detached;
    }

    public void RemoveVersions(IEnumerable<CampaignVersion> versions)
        => db.CampaignVersions.RemoveRange(versions);
}
