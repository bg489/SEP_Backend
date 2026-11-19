using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IMapMetadataRepository : IGenericRepository<MapMetadata>
{
    Task<IReadOnlyList<MapMetadata>> ListActiveAsync(CancellationToken ct = default);
    Task<MapMetadata?> GetActiveByNameAsync(string mapName, CancellationToken ct = default);
}
