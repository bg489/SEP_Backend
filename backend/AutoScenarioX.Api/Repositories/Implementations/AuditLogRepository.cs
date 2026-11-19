using System.Text.Json;
using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class AuditLogRepository(AsxDbContext db)
    : GenericRepository<AuditLog>(db), IAuditLogRepository
{
    public void Write(string entityType, Guid entityId, string action, Guid actorId, object? details = null)
        => Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorId = actorId,
            Details = details is null ? "{}" : JsonSerializer.Serialize(details)
        });

    public async Task<PagedData<AuditLog>> ListForScenarioAsync(Guid scenarioId, int page, int pageSize, CancellationToken ct = default)
    {
        // Gom theo quan hệ dữ liệu (không dò trong details) để dùng được index ix_audit_entity.
        var versionIds = Db.ScenarioVersions.Where(v => v.ScenarioId == scenarioId).Select(v => v.Id);
        var importIds = Db.ImportRecords.Where(r => r.ScenarioId == scenarioId).Select(r => r.Id);
        var batchIds = Db.VariantBatches.Where(b => versionIds.Contains(b.BaseVersionId)).Select(b => b.Id);
        var cloneIds = Db.Scenarios
            .Where(s => s.OriginType == ScenarioOriginTypes.Clone && s.OriginVersionId != null && versionIds.Contains(s.OriginVersionId.Value))
            .Select(s => s.Id);

        var query = Set.AsNoTracking().Where(a =>
            ((a.EntityType == AuditEntityTypes.Scenario || a.EntityType == AuditEntityTypes.Draft) && a.EntityId == scenarioId)
            || (a.EntityType == AuditEntityTypes.Version && versionIds.Contains(a.EntityId))
            || (a.EntityType == AuditEntityTypes.Import && importIds.Contains(a.EntityId))
            || (a.EntityType == AuditEntityTypes.VariantBatch && batchIds.Contains(a.EntityId))
            || (a.EntityType == AuditEntityTypes.Scenario && a.Action == AuditActions.Clone && cloneIds.Contains(a.EntityId)));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedData<AuditLog>(items, total);
    }
}
