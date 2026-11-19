using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IAdministrationRepository
{
    Task<SystemSettings> SettingsAsync(CancellationToken ct = default);
    Task<SystemSettings> SaveSettingsAsync(SystemSettings settings, CancellationToken ct = default);
    Task<OracleConfig> CreateOracleConfigAsync(OracleConfig config, CancellationToken ct = default);
    Task<SystemSettings> ActivateOracleAsync(Guid id, Guid? actorId, CancellationToken ct = default);
    Task<SimilarityAnalysis?> SimilarityAsync(Guid id, CancellationToken ct = default);
    Task SaveSimilarityAsync(SimilarityAnalysis analysis, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> AuditAsync(int page, int pageSize, CancellationToken ct = default);
}
