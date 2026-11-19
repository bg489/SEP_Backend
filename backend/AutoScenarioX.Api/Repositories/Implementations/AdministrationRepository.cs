using System.Data;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public sealed class AdministrationRepository(AsxDbContext db) : IAdministrationRepository
{
    public async Task<SystemSettings> SettingsAsync(CancellationToken ct = default)
        => await db.Set<SystemSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct)
           ?? new SystemSettings();

    public async Task<SystemSettings> SaveSettingsAsync(SystemSettings settings, CancellationToken ct = default)
    {
        var current = await db.Set<SystemSettings>().FindAsync([1], ct);
        if (current is null) db.Add(settings);
        else db.Entry(current).CurrentValues.SetValues(settings);
        Audit("SYSTEM_SETTINGS", Guid.Empty, "UPDATE_SETTINGS", settings.UpdatedBy,
            System.Text.Json.JsonSerializer.Serialize(new { settings.ActiveOracleConfigId, settings.RetentionDays, settings.RetentionEnabled }));
        await db.SaveChangesAsync(ct);
        return settings;
    }

    public async Task<OracleConfig> CreateOracleConfigAsync(OracleConfig config, CancellationToken ct = default)
    {
        // Serialize version allocation, without changing any published immutable configuration.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73114017)", ct);
        config.Version = (await db.OracleConfigs.MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        db.OracleConfigs.Add(config);
        Audit("ORACLE_CONFIG", config.Id, "CREATE_ORACLE_CONFIG", config.CreatedBy, "{}");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return config;
    }

    public async Task<SystemSettings> ActivateOracleAsync(Guid id, Guid? actorId, CancellationToken ct = default)
    {
        if (!await db.OracleConfigs.AnyAsync(x => x.Id == id, ct))
            throw new NotFoundException(ErrorCodes.InvalidRequest, "Oracle configuration was not found.");
        var settings = await SettingsAsync(ct);
        settings.ActiveOracleConfigId = id;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = actorId;
        return await SaveSettingsAsync(settings, ct);
    }

    public Task<SimilarityAnalysis?> SimilarityAsync(Guid id, CancellationToken ct = default)
        => db.Set<SimilarityAnalysis>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task SaveSimilarityAsync(SimilarityAnalysis analysis, CancellationToken ct = default)
    {
        db.Add(analysis);
        Audit("SIMILARITY", analysis.Id, "COMPARE_SCENARIOS", analysis.CreatedBy, "{}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLog>> AuditAsync(int page, int pageSize, CancellationToken ct = default)
        => await db.AuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

    private void Audit(string entity, Guid id, string action, Guid? actorId, string details)
    {
        if (actorId is not { } actor) return;
        db.AuditLogs.Add(new AuditLog { EntityType = entity, EntityId = id, Action = action,
            ActorId = actor, Details = details, CreatedAt = DateTimeOffset.UtcNow });
    }
}
