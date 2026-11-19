using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IScenarioVariantService
{
    Task<VariantBatchResponse> GenerateAsync(Guid scenarioId, int versionNumber, GenerateVariantsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<VariantBatchSummaryResponse>> ListAsync(Guid scenarioId, int versionNumber, CancellationToken ct = default);
    Task<VariantBatchResponse> GetAsync(Guid scenarioId, int versionNumber, Guid batchId, CancellationToken ct = default);
}
