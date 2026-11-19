using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

/// <summary>simulation_runs (Flow 2) nhìn từ phía Oracle: Telemetry đã nhận và các Evaluation.</summary>
public interface IRunRepository
{
    /// <summary>Run kèm campaign, telemetry, evaluations + violations + oracle config (không tracking).</summary>
    Task<SimulationRun?> GetResultAsync(Guid id, CancellationToken ct = default);

    /// <summary>Run kèm campaign, telemetry và evaluations (có tracking) để cập nhật / re-queue.</summary>
    Task<SimulationRun?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Run Queued kế tiếp theo sort_order (có tracking) để worker claim.</summary>
    Task<SimulationRun?> GetNextQueuedAsync(Guid? campaignId, CancellationToken ct = default, Guid? workerId = null);

    /// <summary>Create a linked attempt while preserving previous evidence; caller saves in a transaction.</summary>
    Task<SimulationRun> AddRetryAsync(SimulationRun previous, CancellationToken ct = default);

    /// <summary>Mọi run của campaign, kèm telemetry, scenario và evaluation mới nhất để phân tích lỗi.</summary>
    Task<IReadOnlyList<SimulationRun>> ListForAnalysisAsync(Guid campaignId, CancellationToken ct = default);

    void AddTelemetry(RunTelemetry telemetry);
    void AddEvaluation(Evaluation evaluation);

    /// <summary>Xóa telemetry + evaluations/violations của run (chỉ dùng khi re-queue INFRA).</summary>
    void RemoveInfraArtifacts(SimulationRun run);
}
