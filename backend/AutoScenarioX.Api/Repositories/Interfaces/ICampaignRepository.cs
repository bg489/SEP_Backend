using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface ICampaignRepository
{
    Task<IReadOnlyList<Campaign>> ListAsync(CancellationToken ct = default);
    Task<Campaign?> GetReadOnlyAsync(Guid id, CancellationToken ct = default);
    Task<Campaign?> GetTrackedAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Campaign campaign, CancellationToken ct = default);
    Task AddRunsAsync(IEnumerable<SimulationRun> runs, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationRun>> ListRunsAsync(Guid campaignId, CancellationToken ct = default);
    Task<bool> HasRunsAsync(Guid campaignId, CancellationToken ct = default);
    Task ClearRunsAsync(Guid campaignId, CancellationToken ct = default);
    void RemoveVersions(IEnumerable<CampaignVersion> versions);
}
