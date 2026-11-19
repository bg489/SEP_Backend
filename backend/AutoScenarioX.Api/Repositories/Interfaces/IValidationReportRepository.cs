using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IValidationReportRepository : IGenericRepository<ValidationReport>
{
    Task<ValidationReport?> GetReadOnlyAsync(Guid id, CancellationToken ct = default);
}
