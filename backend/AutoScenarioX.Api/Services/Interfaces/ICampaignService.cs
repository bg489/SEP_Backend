using AutoScenarioX.Api.DTOs.Campaigns;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface ICampaignService
{
    Task<IReadOnlyList<CampaignResponse>> ListAsync(CancellationToken ct = default);
    Task<CampaignResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default);
    Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken ct = default);
    Task<CampaignResponse> StartAsync(Guid id, CancellationToken ct = default);
    Task<CampaignResponse> RerunAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationRunResponse>> ListRunsAsync(Guid campaignId, CancellationToken ct = default);
}
