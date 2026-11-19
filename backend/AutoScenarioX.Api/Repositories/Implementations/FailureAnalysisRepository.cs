using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public sealed class FailureAnalysisRepository(AsxDbContext db) : IFailureAnalysisRepository
{
    public Task<AnalysisSession?> GetByCampaignAsync(Guid campaignId, bool tracking, CancellationToken ct = default)
    {
        var query = db.FailureAnalyses
            .Include(a => a.Campaign)
            .Include(a => a.Faults).ThenInclude(f => f.Runs)
            .AsSplitQuery()
            .Where(a => a.CampaignId == campaignId);
        if (!tracking)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(ct);
    }

    public void Add(AnalysisSession session) => db.FailureAnalyses.Add(session);

    public void Remove(AnalysisSession session) => db.FailureAnalyses.Remove(session);
}
