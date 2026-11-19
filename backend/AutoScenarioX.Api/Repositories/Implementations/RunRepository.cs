using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public sealed class RunRepository(AsxDbContext db) : IRunRepository
{
    public Task<SimulationRun?> GetResultAsync(Guid id, CancellationToken ct = default)
        => db.SimulationRuns.AsNoTracking()
            .Include(r => r.Campaign)
            .Include(r => r.Telemetry)
            .Include(r => r.Evaluations).ThenInclude(e => e.Violations)
            .Include(r => r.Evaluations).ThenInclude(e => e.OracleConfig)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<SimulationRun?> GetForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
        return await db.SimulationRuns
            .Include(r => r.Campaign).ThenInclude(c => c.Runs)
            .Include(r => r.Telemetry)
            .Include(r => r.Evaluations).ThenInclude(e => e.Violations)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<SimulationRun>> ListForAnalysisAsync(Guid campaignId, CancellationToken ct = default)
        => await db.SimulationRuns.AsNoTracking()
            .Include(r => r.Telemetry)
            .Include(r => r.ScenarioVersion).ThenInclude(v => v.Scenario)
            .Include(r => r.Evaluations).ThenInclude(e => e.Violations)
            .AsSplitQuery()
            .Where(r => r.CampaignId == campaignId && r.IsCurrent)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);

    public async Task<SimulationRun?> GetNextQueuedAsync(Guid? campaignId, CancellationToken ct = default, Guid? workerId = null)
    {
        // The function locks and updates in ONE SQL statement, not a SELECT then UPDATE race.
        var id = await db.Database.SqlQuery<Guid?>($"SELECT asx.claim_next_run({campaignId}, {workerId}) AS \"Value\"")
            .SingleAsync(ct);
        return id is null ? null : await GetForUpdateAsync(id.Value, ct);
    }

    public async Task<SimulationRun> AddRetryAsync(SimulationRun previous, CancellationToken ct = default)
    {
        if (!previous.IsCurrent)
        {
            return await db.SimulationRuns.FirstAsync(r => r.CampaignId == previous.CampaignId
                && r.ScenarioVersionId == previous.ScenarioVersionId && r.IsCurrent, ct);
        }
        previous.IsCurrent = false;
        // Flush the old current flag before inserting the new row under the filtered unique index.
        await db.SaveChangesAsync(ct);
        var retry = new SimulationRun
        {
            Id = Guid.NewGuid(), CampaignId = previous.CampaignId, Campaign = previous.Campaign,
            ScenarioVersionId = previous.ScenarioVersionId, SortOrder = previous.SortOrder,
            ContentHash = previous.ContentHash, Seed = previous.Seed, Attempt = previous.Attempt + 1,
            PreviousRunId = previous.Id, IsCurrent = true, InfraRetryCount = previous.InfraRetryCount,
            Status = SimulationRunStatuses.Queued,
        };
        await db.SimulationRuns.AddAsync(retry, ct);
        previous.Campaign.Runs.Add(retry);
        return retry;
    }

    public void AddTelemetry(RunTelemetry telemetry) => db.RunTelemetry.Add(telemetry);

    public void AddEvaluation(Evaluation evaluation) => db.Evaluations.Add(evaluation);

    public void RemoveInfraArtifacts(SimulationRun run)
    {
        foreach (var evaluation in run.Evaluations.ToList())
        {
            db.Violations.RemoveRange(evaluation.Violations);
            db.Evaluations.Remove(evaluation);
        }
        run.Evaluations.Clear();
        if (run.Telemetry is not null)
        {
            db.RunTelemetry.Remove(run.Telemetry);
            run.Telemetry = null;
        }
    }
}
