using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public sealed class OracleConfigRepository(AsxDbContext db) : IOracleConfigRepository
{
    public async Task<IReadOnlyList<OracleConfig>> ListAsync(CancellationToken ct = default)
        => await db.OracleConfigs.AsNoTracking().OrderByDescending(x => x.Version).ToListAsync(ct);

    public Task<OracleConfig?> GetAsync(Guid id, CancellationToken ct = default)
        => db.OracleConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<OracleConfig?> GetLatestAsync(CancellationToken ct = default)
    {
        var active = await db.Set<SystemSettings>().Where(x => x.Id == 1)
            .Select(x => x.ActiveOracleConfigId).SingleOrDefaultAsync(ct);
        return active is { } id
            ? await GetAsync(id, ct)
            : await db.OracleConfigs.AsNoTracking().OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    }
}
