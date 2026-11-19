using AutoScenarioX.Api.DTOs.Oracle;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IOracleService
{
    Task<IReadOnlyList<OracleConfigResponse>> ListConfigsAsync(CancellationToken ct = default);

    Task<RunResultResponse> GetRunResultAsync(Guid runId, CancellationToken ct = default);

    Task<ClaimedRunResponse?> ClaimNextAsync(Guid? campaignId, CancellationToken ct = default, Guid? workerId = null);
    Task<RunProgressResponse> GetProgressAsync(Guid runId, CancellationToken ct = default);
    Task<RunProgressResponse> SaveProgressAsync(Guid runId, RunProgressRequest request, CancellationToken ct = default);

    Task<ClaimedRunResponse> MarkRunningAsync(Guid runId, CancellationToken ct = default);

    Task<RunResultResponse> SubmitTelemetryAsync(Guid runId, Stream body, CancellationToken ct = default);

    /// <summary>
    /// Re-queue after INFRA_FAILED by creating a linked attempt and preserving previous evidence.
    /// Không dùng cho PASS/FAIL oracle.
    /// </summary>
    Task<RequeueRunResponse> RequeueInfraAsync(Guid runId, CancellationToken ct = default);

    Task SaveVideoAsync(Guid runId, Stream body, CancellationToken ct = default);

    /// <summary>Trả null nếu chưa có video.</summary>
    Task<(Stream Stream, string ContentType)?> OpenVideoAsync(Guid runId, CancellationToken ct = default);
}
