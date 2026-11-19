using AutoScenarioX.Api.DTOs.Oracle;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IAdminOracleService
{
    Task<IReadOnlyList<AdminOracleConfigResponse>> ListAsync(CancellationToken ct = default);
    Task<AdminOracleConfigResponse> CreateAsync(CreateOracleConfigRequest request, CancellationToken ct = default);
    Task<SystemSettingsResponse> ActivateAsync(Guid id, CancellationToken ct = default);
    Task<SystemSettingsResponse> SettingsAsync(CancellationToken ct = default);
    Task<SystemSettingsResponse> UpdateSettingsAsync(UpdateSystemSettingsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntryResponse>> AuditAsync(int page, int pageSize, CancellationToken ct = default);
}
