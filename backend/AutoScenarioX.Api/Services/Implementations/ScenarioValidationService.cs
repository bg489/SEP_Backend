using System.Text.Json;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioValidationService(
    IUnitOfWork uow,
    IScenarioValidator validator,
    ICanonicalJsonHasher hasher,
    ICurrentUser currentUser) : IScenarioValidationService
{
    public async Task<ValidationReportResponse> ValidateDraftAsync(Guid scenarioId, CancellationToken ct = default)
    {
        var scenario = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        if (scenario.Status == ScenarioStatuses.Archived)
            throw new BusinessRuleException(ErrorCodes.ScenarioArchived,
                "This scenario is archived. Restore it before editing.");

        var draft = await uow.ScenarioDrafts.GetForUpdateAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.DraftNotFound, "This scenario has no draft.");

        using var document = JsonDocument.Parse(draft.Content);
        var raw = document.RootElement;

        // Chuẩn hoá trước: hash phải tính được kể cả khi nội dung chưa hợp lệ
        // (validation_reports.content_hash là NOT NULL).
        var normalized = ScenarioContentNormalizer.Normalize(raw);
        var contentHash = hasher.ComputeHash(normalized);

        var mapName = ScenarioContentReader.AsString(normalized["map"]?["name"]);
        var maps = await uow.MapMetadata.ListActiveAsync(ct);
        var map = maps.FirstOrDefault(m => m.MapName == mapName);

        var schemaVersion = raw.TryGetProperty("schema_version", out var declared) && declared.ValueKind == JsonValueKind.String
            ? declared.GetString()!
            : ScenarioCatalog.CurrentSchemaVersion;

        var result = await validator.ValidateAsync(new ScenarioValidationContext
        {
            Raw = raw,
            Normalized = normalized,
            MapName = mapName,
            Map = map,
            MapMetadataAvailable = maps.Count > 0
        }, ct);

        // Báo sớm nội dung trùng một version đã có, thay vì đợi publish mới gặp 409
        var duplicate = await uow.ScenarioVersions.FindByContentHashAsync(scenarioId, contentHash, ct);

        var report = new ValidationReport
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenarioId,
            ContentHash = contentHash,
            IsValid = result.IsValid,
            StoppedAt = result.StoppedAt?.ToDbValue(),
            Errors = result.Errors.ToJsonArray(),
            Warnings = result.Warnings.ToJsonArray(),
            SchemaVersion = schemaVersion,
            ValidatorVersion = ValidatorVersion.Current,
            MapMetadataId = map?.Id,
            CreatedBy = currentUser.UserId
        };

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.ValidationReports.Add(report);
            draft.LastValidationReportId = report.Id;
            uow.AuditLogs.Write(AuditEntityTypes.Draft, scenarioId, AuditActions.Validate, currentUser.UserId,
                new { is_valid = report.IsValid, stopped_at = report.StoppedAt, content_hash = report.ContentHash });

            await uow.SaveChangesAsync(token);
            return report.ToResponse(result.Errors, result.Warnings, duplicate?.VersionNumber);
        }, ct);
    }
}
