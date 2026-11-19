using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IFailureAnalysisRepository
{
    Task<AnalysisSession?> GetByCampaignAsync(Guid campaignId, bool tracking, CancellationToken ct = default);

    void Add(AnalysisSession session);

    void Remove(AnalysisSession session);
}
