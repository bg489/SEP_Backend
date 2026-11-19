using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioVersionService(
    IUnitOfWork uow,
    ICanonicalJsonHasher hasher,
    ICurrentUser currentUser) : IScenarioVersionService
{
    public async Task<IReadOnlyList<ScenarioVersionSummaryResponse>> ListAsync(Guid scenarioId, CancellationToken ct = default)
    {
        await EnsureScenarioExistsAsync(scenarioId, ct);
        var versions = await uow.ScenarioVersions.ListByScenarioAsync(scenarioId, ct);
        return versions.Select(v => v.ToSummaryResponse()).ToList();
    }

    public async Task<ScenarioVersionDetailResponse> GetByNumberAsync(Guid scenarioId, int versionNumber, CancellationToken ct = default)
    {
        await EnsureScenarioExistsAsync(scenarioId, ct);
        return (await LoadVersionAsync(scenarioId, versionNumber, ct)).ToDetailResponse();
    }

    public async Task<ScenarioVersionDetailResponse> PublishAsync(Guid scenarioId, PublishVersionRequest request, CancellationToken ct = default)
    {
        var changeNote = string.IsNullOrWhiteSpace(request.ChangeNote) ? null : request.ChangeNote.Trim();
        if (changeNote is { Length: > 1000 })
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["changeNote"] = ["Change notes must be at most 1,000 characters"]
            });

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            // Khoá dòng scenario: chặn hai người cùng publish -> tránh trùng version_number
            await uow.Scenarios.LockForUpdateAsync(scenarioId, token);

            var scenario = await uow.Scenarios.FindAsync([scenarioId], token)
                ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

            if (scenario.Status == ScenarioStatuses.Archived)
                throw new BusinessRuleException(ErrorCodes.ScenarioArchived,
                    "This scenario is archived. Restore it before publishing.");

            var draft = await uow.ScenarioDrafts.GetForUpdateAsync(scenarioId, token)
                ?? throw new NotFoundException(ErrorCodes.DraftNotFound,
                    "There is no draft to publish.");

            // Chuẩn hoá rồi mới tính hash: hai bản nháp tương đương phải ra cùng một hash
            using var document = JsonDocument.Parse(draft.Content);
            var normalized = ScenarioContentNormalizer.Normalize(document.RootElement);
            var contentHash = hasher.ComputeHash(normalized);

            var report = await LoadValidReportAsync(draft, scenarioId, contentHash, token);

            if (await uow.ScenarioVersions.FindByContentHashAsync(scenarioId, contentHash, token) is { } duplicate)
                throw new ConflictException(ErrorCodes.DuplicateVersion,
                    $"Content duplicates version {duplicate.VersionNumber}. Change the configuration or reuse version {duplicate.VersionNumber}.");

            // Scenario nhập từ file: version phải trỏ về lần import đã sinh ra nó (truy xuất nguồn).
            var importRecordId = ScenarioContentReader.AsString(normalized["source"]?["type"]) == ScenarioOriginTypes.Import
                ? (await uow.ImportRecords.GetSucceededByScenarioAsync(scenarioId, token))?.Id
                : null;

            var version = BuildVersion(scenario, normalized, contentHash, report, changeNote, importRecordId);
            uow.ScenarioVersions.Add(version);

            // Version cũ vẫn READY: "mới nhất" là con trỏ latest_version_id, không phải trạng thái.
            // DEPRECATED chỉ do người dùng chủ động ngừng dùng (DeprecateAsync).
            scenario.LatestVersionId = version.Id;
            scenario.LatestVersionNumber = version.VersionNumber;

            uow.ScenarioDrafts.Remove(draft);
            uow.AuditLogs.Write(AuditEntityTypes.Version, version.Id, AuditActions.Publish, currentUser.UserId,
                new { scenario_id = scenarioId, version_number = version.VersionNumber, content_hash = contentHash });

            await uow.SaveChangesAsync(token);
            return version.ToDetailResponse();
        }, ct);
    }

    public async Task<ScenarioVersionSummaryResponse> DeprecateAsync(Guid scenarioId, int versionNumber, CancellationToken ct = default)
    {
        await EnsureScenarioExistsAsync(scenarioId, ct);
        var existing = await LoadVersionAsync(scenarioId, versionNumber, ct);

        var version = await uow.ScenarioVersions.FindAsync([existing.Id], ct)
            ?? throw new NotFoundException(ErrorCodes.VersionNotFound, $"Version {versionNumber} was not found.");

        if (version.Status == VersionStatuses.Deprecated)
            throw new ConflictException(ErrorCodes.VersionAlreadyDeprecated,
                $"Version {versionNumber} is already DEPRECATED.");

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            MarkDeprecated(version);
            uow.AuditLogs.Write(AuditEntityTypes.Version, version.Id, AuditActions.Deprecate, currentUser.UserId,
                new { scenario_id = scenarioId, version_number = versionNumber });

            await uow.SaveChangesAsync(token);
            return version.ToSummaryResponse();
        }, ct);
    }

    public async Task<ScenarioVersionDiffResponse> DiffAsync(Guid scenarioId, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        await EnsureScenarioExistsAsync(scenarioId, ct);

        if (fromVersion == toVersion)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["to"] = ["Select two different versions."]
            });

        var from = await LoadVersionAsync(scenarioId, fromVersion, ct);
        var to = await LoadVersionAsync(scenarioId, toVersion, ct);

        var changes = JsonDiff.Compare(JsonNode.Parse(from.Content), JsonNode.Parse(to.Content));

        return new ScenarioVersionDiffResponse(
            scenarioId, fromVersion, toVersion,
            from.ContentHash, to.ContentHash,
            ExecutionChanged: from.ContentHash != to.ContentHash,
            Changes: changes);
    }

    /// <summary>Report phải tồn tại, hợp lệ và tính trên đúng nội dung sắp publish.</summary>
    private async Task<ValidationReport> LoadValidReportAsync(ScenarioDraft draft, Guid scenarioId, string contentHash, CancellationToken ct)
    {
        if (draft.LastValidationReportId is not { } reportId)
            throw new BusinessRuleException(ErrorCodes.ValidationRequired,
                "Validate the draft before publishing.");

        var report = await uow.ValidationReports.GetReadOnlyAsync(reportId, ct)
            ?? throw new BusinessRuleException(ErrorCodes.ValidationRequired,
                "The draft validation report was not found. Validate again.");

        if (!report.IsValid)
            throw new BusinessRuleException(ErrorCodes.ValidationRequired,
                $"The draft has errors in the {report.StoppedAt} tier. Fix them and validate again.");

        if (report.ScenarioId != scenarioId || report.ContentHash != contentHash)
            throw new BusinessRuleException(ErrorCodes.ContentChangedSinceValidation,
                "Content has changed since the last validation. Validate again before publishing.");

        return report;
    }

    private ScenarioVersion BuildVersion(
        Scenario scenario, JsonObject normalized, string contentHash, ValidationReport report, string? changeNote,
        Guid? importRecordId = null)
    {
        var source = normalized["source"] as JsonObject;
        var sourceType = ReadSourceType(source);
        var parentVersionId = ParseGuid(source?["parent_version_id"]) ?? scenario.LatestVersionId;

        if (sourceType is ScenarioOriginTypes.Clone or ScenarioOriginTypes.Variant && parentVersionId is null)
            throw new BusinessRuleException(ErrorCodes.ConstraintViolation,
                $"Unable to identify the parent version for source.type = '{sourceType}'.");

        return new ScenarioVersion
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenario.Id,
            VersionNumber = scenario.LatestVersionNumber + 1,
            SchemaVersion = report.SchemaVersion,
            // Lưu đúng dạng canonical đã dùng để tính hash -> nội dung trong DB luôn khớp content_hash
            Content = CanonicalJsonSerializer.Serialize(normalized),
            ContentHash = contentHash,
            Status = VersionStatuses.Ready,
            ChangeNote = changeNote,
            SourceType = sourceType,
            ParentVersionId = sourceType is ScenarioOriginTypes.Clone or ScenarioOriginTypes.Variant ? parentVersionId : null,
            ImportRecordId = importRecordId,
            ValidationReportId = report.Id,
            PublishedBy = currentUser.UserId
        };
    }

    private void MarkDeprecated(ScenarioVersion version)
    {
        version.Status = VersionStatuses.Deprecated;
        version.DeprecatedBy = currentUser.UserId;
        version.DeprecatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<ScenarioVersion> LoadVersionAsync(Guid scenarioId, int versionNumber, CancellationToken ct)
        => await uow.ScenarioVersions.GetByNumberAsync(scenarioId, versionNumber, ct)
           ?? throw new NotFoundException(ErrorCodes.VersionNotFound,
               $"Scenario {scenarioId} has no version {versionNumber}.");

    private async Task EnsureScenarioExistsAsync(Guid scenarioId, CancellationToken ct)
    {
        if (!await uow.Scenarios.ExistsAsync(scenarioId, ct))
            throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");
    }

    private static string ReadSourceType(JsonObject? source)
    {
        var value = ScenarioContentReader.AsString(source?["type"]);
        return string.IsNullOrWhiteSpace(value) ? ScenarioOriginTypes.Manual : value;
    }

    private static Guid? ParseGuid(JsonNode? node)
        => Guid.TryParse(ScenarioContentReader.AsString(node), out var id) ? id : null;
}
