using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class ScenarioDraftRepository(AsxDbContext db)
    : GenericRepository<ScenarioDraft>(db), IScenarioDraftRepository
{
    public Task<ScenarioDraft?> GetReadOnlyAsync(Guid scenarioId, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(d => d.ScenarioId == scenarioId, ct);

    public Task<ScenarioDraft?> GetForUpdateAsync(Guid scenarioId, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(d => d.ScenarioId == scenarioId, ct);
}
