using AutoScenarioX.Api.DTOs.Prioritization;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IPrioritizationService
{
    IReadOnlyList<PrioritizationStrategyInfo> ListStrategies();
    Task<PrioritizationRunResponse> RankAsync(PrioritizeRequest request, CancellationToken ct = default);
}
