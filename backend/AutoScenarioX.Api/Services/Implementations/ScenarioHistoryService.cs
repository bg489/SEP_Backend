using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Validators.ScenarioContent;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioHistoryService(IUnitOfWork uow, IOptions<ScenarioOptions> options) : IScenarioHistoryService
{
    public const int DefaultPageSize = 50;

    public async Task<PagedResult<ScenarioHistoryEventResponse>> GetAsync(
        Guid scenarioId, int page, int? pageSize, CancellationToken ct = default)
    {
        _ = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");
        page = Math.Clamp(page, 1, 100_000);   // chặn trần để (page - 1) * pageSize không tràn int
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, options.Value.MaxPageSize);

        var data = await uow.AuditLogs.ListForScenarioAsync(scenarioId, page, size, ct);
        var details = data.Items.ToDictionary(a => a.Id, a => JsonNode.Parse(a.Details) as JsonObject ?? new JsonObject());

        var batchIds = data.Items.Where(a => a.EntityType == AuditEntityTypes.VariantBatch).Select(a => a.EntityId).Distinct().ToList();
        var batchBase = (await uow.VariantBatches.ListByIdsAsync(batchIds, ct)).ToDictionary(b => b.Id, b => b.BaseVersionId);

        var versionIds = data.Items
            .Select(a => VersionIdOf(a, details[a.Id], batchBase))
            .OfType<Guid>().Distinct().ToList();
        var versions = (await uow.ScenarioVersions.ListRefsAsync(versionIds, ct)).ToDictionary(v => v.Id);

        var relatedIds = data.Items
            .Select(a => RelatedScenarioIdOf(a, scenarioId, details[a.Id], versions))
            .OfType<Guid>().Distinct().ToList();
        var scenarios = (await uow.Scenarios.ListRefsAsync(relatedIds, ct)).ToDictionary(s => s.Id);
        var actors = (await uow.AppUsers.ListByIdsAsync(data.Items.Select(a => a.ActorId).Distinct().ToList(), ct))
            .ToDictionary(u => u.Id);

        var items = data.Items.Select(a =>
        {
            var d = details[a.Id];
            var versionNumber = VersionIdOf(a, d, batchBase) is { } versionId && versions.TryGetValue(versionId, out var version)
                ? version.VersionNumber
                : IntOf(d, "base_version_number") ?? IntOf(d, "source_version_number");
            var related = RelatedScenarioIdOf(a, scenarioId, d, versions) is { } relatedId
                ? scenarios.GetValueOrDefault(relatedId)
                : null;
            return a.ToHistoryResponse(actors.GetValueOrDefault(a.ActorId), versionNumber, related);
        }).ToList();
        return new PagedResult<ScenarioHistoryEventResponse>(items, page, size, data.TotalCount);
    }

    /// <summary>Version gắn với sự kiện: chính version (VERSION), version gốc (batch biến thể), version cha (scenario tạo làm biến thể).</summary>
    private static Guid? VersionIdOf(AuditLog log, JsonObject details, IReadOnlyDictionary<Guid, Guid> batchBase) => log.EntityType switch
    {
        AuditEntityTypes.Version => log.EntityId,
        AuditEntityTypes.VariantBatch => batchBase.TryGetValue(log.EntityId, out var baseVersionId) ? baseVersionId : null,
        AuditEntityTypes.Scenario when log.Action == AuditActions.Create => GuidOf(details, "parent_version_id"),
        _ => null
    };

    /// <summary>CLONE của chính scenario → scenario nguồn; CLONE tạo scenario khác → bản clone; biến thể → scenario cha.</summary>
    private static Guid? RelatedScenarioIdOf(AuditLog log, Guid scenarioId, JsonObject details, IReadOnlyDictionary<Guid, VersionRef> versions)
    {
        if (log.EntityType != AuditEntityTypes.Scenario) return null;
        if (log.Action == AuditActions.Clone)
            return log.EntityId == scenarioId ? GuidOf(details, "source_scenario_id") : log.EntityId;
        if (log.Action == AuditActions.Create && GuidOf(details, "parent_version_id") is { } parent &&
            versions.TryGetValue(parent, out var version))
            return version.ScenarioId;
        return null;
    }

    private static Guid? GuidOf(JsonObject details, string key)
        => Guid.TryParse(ScenarioContentReader.AsString(details[key]), out var value) ? value : null;

    private static int? IntOf(JsonObject details, string key)
        => ScenarioContentReader.TryGetNumber(details[key], new JsonObject(), out var value) ? (int)value : null;
}
