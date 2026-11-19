using AutoScenarioX.Api.DTOs.FailureAnalysis;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IFailureAnalysisService
{
    Task<FailureAnalysisResponse> GetAsync(Guid campaignId, CancellationToken ct = default);

    Task<FailureAnalysisResponse> GroupAsync(Guid campaignId, CancellationToken ct = default);

    Task<FailureAnalysisResponse> VerifyAsync(Guid campaignId, CancellationToken ct = default);

    Task<FailureAnalysisResponse> FindSimilarAsync(Guid campaignId, CancellationToken ct = default);

    Task<FailureAnalysisResponse> CheckExplainabilityAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>Thao tác riêng: đưa các fault chưa giải thích được về Flow 3.</summary>
    Task<FailureAnalysisResponse> ReturnToFlow3Async(Guid campaignId, CancellationToken ct = default);

    Task<FailureAnalysisResponse> GenerateAsync(Guid campaignId, CancellationToken ct = default);

    Task<FailureAnalysisResponse> ReviewAsync(Guid campaignId, ReviewFailureReportRequest request, CancellationToken ct = default);

    Task<EffectivenessResponse> GetEvaluationAsync(Guid campaignId, CancellationToken ct = default);
}
