using AutoScenarioX.Api.DTOs.Workers;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IWorkerManagementService
{
    Task<IReadOnlyList<WorkerResponse>> ListAsync(CancellationToken ct = default);
    Task<WorkerResponse> RegisterAsync(RegisterWorkerRequest request, CancellationToken ct = default);
    Task<WorkerResponse> HeartbeatAsync(Guid id, WorkerHeartbeatRequest request, CancellationToken ct = default);
    Task<WorkerResponse> UpdateAsync(Guid id, UpdateWorkerRequest request, CancellationToken ct = default);
    Task RecoverLostAsync(CancellationToken ct = default);
}
