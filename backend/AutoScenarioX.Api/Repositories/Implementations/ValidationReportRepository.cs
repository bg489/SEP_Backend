using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class ValidationReportRepository(AsxDbContext db)
    : GenericRepository<ValidationReport>(db), IValidationReportRepository
{
    public Task<ValidationReport?> GetReadOnlyAsync(Guid id, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
}
