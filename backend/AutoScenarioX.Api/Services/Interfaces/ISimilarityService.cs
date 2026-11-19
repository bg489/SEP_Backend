using AutoScenarioX.Api.DTOs.Prioritization;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface ISimilarityService
{
    Task<SimilarityResponse> CompareAsync(CompareScenariosRequest request, CancellationToken ct = default);
    Task<SimilarityResponse> GetAsync(Guid id, CancellationToken ct = default);
}
