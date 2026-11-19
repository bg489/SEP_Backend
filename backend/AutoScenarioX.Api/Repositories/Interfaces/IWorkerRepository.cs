using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IWorkerRepository
{
    Task<IReadOnlyList<SimulationWorker>> ListAsync(CancellationToken ct = default);
    Task<SimulationWorker?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(SimulationWorker worker, CancellationToken ct = default);
    Task<int> ActiveRunsAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    Task<int> RecoverLostAsync(CancellationToken ct = default);
}
