using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Formats;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

/// <summary>
/// Xuất nội dung scenario ra file.
///
/// Xuất NGUYÊN VĂN nội dung đã lưu, không chuẩn hoá lại và không bỏ bớt khối nào:
/// file xuất ra phải import lại được và cho đúng content_hash cũ.
/// </summary>
public sealed class ScenarioExportService(
    IUnitOfWork uow,
    IScenarioFormatRegistry formats,
    ICurrentUser currentUser) : IScenarioExportService
{
    public async Task<ScenarioExportResult> ExportVersionAsync(
        Guid scenarioId, int versionNumber, string? format, CancellationToken ct = default)
    {
        var scenario = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        var version = await uow.ScenarioVersions.GetByNumberAsync(scenarioId, versionNumber, ct)
            ?? throw new NotFoundException(ErrorCodes.VersionNotFound,
                $"Scenario {scenario.Code} has no version {versionNumber}.");

        var adapter = ResolveAdapter(format);
        var content = Parse(version.Content);
        var fileName = $"{scenario.Code}_v{version.VersionNumber}{adapter.DefaultExtension}";
        var serialized = adapter.Serialize(content);

        uow.AuditLogs.Write(AuditEntityTypes.Version, version.Id, AuditActions.Export, currentUser.UserId,
            new { format = adapter.Format, version = version.VersionNumber, content_hash = version.ContentHash });
        await uow.SaveChangesAsync(ct);

        return new ScenarioExportResult(fileName, adapter.MediaType, serialized);
    }

    public async Task<ScenarioExportResult> ExportDraftAsync(Guid scenarioId, string? format, CancellationToken ct = default)
    {
        var scenario = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        var draft = await uow.ScenarioDrafts.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.DraftNotFound, "This scenario has no draft.");

        var adapter = ResolveAdapter(format);
        var content = Parse(draft.Content);
        var fileName = $"{scenario.Code}_draft{adapter.DefaultExtension}";
        var serialized = adapter.Serialize(content);

        uow.AuditLogs.Write(AuditEntityTypes.Draft, scenarioId, AuditActions.Export, currentUser.UserId,
            new { format = adapter.Format, row_version = draft.RowVersion });
        await uow.SaveChangesAsync(ct);

        return new ScenarioExportResult(fileName, adapter.MediaType, serialized);
    }

    private IScenarioFormatAdapter ResolveAdapter(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return formats.ByFormat(ScenarioFormats.Json)!;

        return formats.ByFormat(format)
            ?? throw new BusinessRuleException(ErrorCodes.ImportFormatUnsupported,
                $"Format '{format}' is not supported. Supported formats: {string.Join(", ", formats.SupportedFormats)}.");
    }

    /// <summary>Nội dung trong DB là jsonb đã qua validate nên hỏng ở đây là lỗi hệ thống, không phải lỗi người dùng.</summary>
    private static JsonObject Parse(string json)
        => JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Nội dung scenario trong DB không phải JSON object.");
}
