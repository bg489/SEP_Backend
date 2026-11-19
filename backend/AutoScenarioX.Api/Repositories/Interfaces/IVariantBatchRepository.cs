using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IVariantBatchRepository : IGenericRepository<VariantBatch>
{
    Task<IReadOnlyList<VariantBatch>> ListByVersionAsync(Guid baseVersionId, CancellationToken ct = default);
    Task<VariantBatch?> GetReadOnlyAsync(Guid id, Guid baseVersionId, CancellationToken ct = default);
    Task<IReadOnlyList<Scenario>> ListScenariosAsync(Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyList<VariantBatch>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}
