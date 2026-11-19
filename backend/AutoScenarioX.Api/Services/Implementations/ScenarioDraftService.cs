using System.Text;
using System.Text.Json;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioDraftService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IOptions<ScenarioOptions> options) : IScenarioDraftService
{
    private readonly ScenarioOptions _options = options.Value;

    public async Task<ScenarioDraftResponse> GetAsync(Guid scenarioId, CancellationToken ct = default)
    {
        if (!await uow.Scenarios.ExistsAsync(scenarioId, ct))
            throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        var draft = await uow.ScenarioDrafts.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.DraftNotFound, "This scenario has no draft.");

        return draft.ToResponse();
    }

    public async Task<ScenarioDraftResponse> CreateAsync(Guid scenarioId, int? fromVersionNumber = null, CancellationToken ct = default)
    {
        if (fromVersionNumber is < 1)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["fromVersion"] = ["The version number must be at least 1"]
            });

        var scenario = await LoadEditableScenarioAsync(scenarioId, ct);

        if (await uow.ScenarioDrafts.GetReadOnlyAsync(scenarioId, ct) is not null)
            throw new ConflictException(ErrorCodes.DraftAlreadyExists,
                "This scenario already has a draft. Continue editing or discard the existing draft.");

        // Mặc định sửa tiếp từ version mới nhất; có fromVersion thì sửa từ version đó (kể cả DEPRECATED,
        // để sửa lại bản đã ngừng dùng). Scenario chưa có version nào thì dùng bản nháp mẫu.
        ScenarioVersion? baseVersion = fromVersionNumber is { } number
            ? await uow.ScenarioVersions.GetByNumberAsync(scenarioId, number, ct)
              ?? throw new NotFoundException(ErrorCodes.VersionNotFound, $"This scenario has no version {number}.")
            : scenario.LatestVersionId is { } versionId
                ? await uow.ScenarioVersions.GetReadOnlyAsync(versionId, ct)
                  ?? throw new NotFoundException(ErrorCodes.VersionNotFound, "The latest version of this scenario was not found.")
                : null;

        var draft = new ScenarioDraft
        {
            ScenarioId = scenarioId,
            Content = baseVersion?.Content ?? ScenarioContentTemplate.Create(scenario.Name, scenario.Category),
            BaseVersionId = baseVersion?.Id,
            UpdatedBy = currentUser.UserId
        };

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.ScenarioDrafts.Add(draft);
            uow.AuditLogs.Write(AuditEntityTypes.Draft, scenarioId, AuditActions.Create, currentUser.UserId,
                new { base_version_id = baseVersion?.Id, base_version_number = baseVersion?.VersionNumber });
            await uow.SaveChangesAsync(token);
            return draft.ToResponse();
        }, ct);
    }

    public async Task<ScenarioDraftResponse> SaveAsync(Guid scenarioId, SaveDraftRequest request, int ifMatchRowVersion, CancellationToken ct = default)
    {
        await LoadEditableScenarioAsync(scenarioId, ct);

        var draft = await uow.ScenarioDrafts.GetForUpdateAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.DraftNotFound, "This scenario has no draft.");

        if (draft.RowVersion != ifMatchRowVersion)
            throw new ConflictException(ErrorCodes.ConcurrencyConflict,
                $"The draft has changed (current revision {draft.RowVersion}, submitted {ifMatchRowVersion}). Load the latest draft before saving again.");

        var content = NormalizeContent(request);

        draft.Content = content;
        draft.RowVersion += 1;                  // DB không tự tăng, chỉ có trigger cho updated_at
        draft.UpdatedBy = currentUser.UserId;
        draft.LastValidationReportId = null;    // nội dung đổi -> report cũ hết hiệu lực

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.AuditLogs.Write(AuditEntityTypes.Draft, scenarioId, AuditActions.UpdateDraft, currentUser.UserId,
                new { row_version = draft.RowVersion });
            await uow.SaveChangesAsync(token);
            return draft.ToResponse();
        }, ct);
    }

    public async Task DeleteAsync(Guid scenarioId, CancellationToken ct = default)
    {
        if (!await uow.Scenarios.ExistsAsync(scenarioId, ct))
            throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        var draft = await uow.ScenarioDrafts.GetForUpdateAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.DraftNotFound, "This scenario has no draft.");

        await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.ScenarioDrafts.Remove(draft);
            uow.AuditLogs.Write(AuditEntityTypes.Draft, scenarioId, AuditActions.DiscardDraft, currentUser.UserId);
            await uow.SaveChangesAsync(token);
            return true;
        }, ct);
    }

    /// <summary>Lấy scenario và chặn thao tác ghi khi đã lưu trữ.</summary>
    private async Task<Scenario> LoadEditableScenarioAsync(Guid scenarioId, CancellationToken ct)
    {
        var scenario = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        if (scenario.Status == ScenarioStatuses.Archived)
            throw new BusinessRuleException(ErrorCodes.ScenarioArchived,
                "This scenario is archived. Restore it before editing.");

        return scenario;
    }

    /// <summary>Kiểm tra content là JSON object và không vượt giới hạn kích thước.</summary>
    private string NormalizeContent(SaveDraftRequest request)
    {
        if (request.Content.ValueKind != JsonValueKind.Object)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["content"] = ["Scenario content must be a JSON object."]
            });

        var raw = request.Content.GetRawText();
        var maxBytes = _options.MaxContentSizeKb * 1024;
        if (Encoding.UTF8.GetByteCount(raw) > maxBytes)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["content"] = [$"Content exceeds the limit of {_options.MaxContentSizeKb} KB."]
            });

        return raw;
    }
}
