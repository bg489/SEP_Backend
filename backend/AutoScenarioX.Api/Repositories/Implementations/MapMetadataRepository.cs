using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class MapMetadataRepository(AsxDbContext db)
    : GenericRepository<MapMetadata>(db), IMapMetadataRepository
{
    public async Task<IReadOnlyList<MapMetadata>> ListActiveAsync(CancellationToken ct = default)
        => await Set.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.MapName).ToListAsync(ct);

    public Task<MapMetadata?> GetActiveByNameAsync(string mapName, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(m => m.IsActive && m.MapName == mapName, ct);
}
