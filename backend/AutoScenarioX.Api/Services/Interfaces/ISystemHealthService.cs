using AutoScenarioX.Api.DTOs.Common;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface ISystemHealthService
{
    Task<HealthResponse> CheckDatabaseAsync(CancellationToken ct = default);
}
