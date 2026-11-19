using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Formats;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Validators.ScenarioContent;
using Microsoft.Extensions.Options;
using AutoScenarioX.Api.Services.Tags;

namespace AutoScenarioX.Api.Services.Implementations;

/// <summary>
/// Nạp scenario từ file ngoài.
///
/// Luồng: đọc file -> chọn adapter -> parse sang JSON nội bộ -> ghi đè khối source
/// (để truy xuất nguồn) -> chuẩn hoá -> validate 3 tầng -> tạo scenario + bản nháp.
/// Import KHÔNG tự publish: kỹ sư phải xem lại rồi mới publish, đúng tinh thần
/// "Flow 1 kết thúc khi có version READY do người quyết định".
///
/// Mọi lần import đều ghi asx.import_records, kể cả thất bại - đó là yêu cầu truy xuất nguồn.
/// </summary>
public sealed class ScenarioImportService(
    IUnitOfWork uow,
    IScenarioFormatRegistry formats,
    IScenarioValidator validator,
    ICanonicalJsonHasher hasher,
    ICurrentUser currentUser,
    IOptions<ScenarioOptions> options) : IScenarioImportService
{
    private readonly ScenarioOptions _options = options.Value;

    public async Task<ScenarioImportResponse> ImportAsync(ImportScenarioRequest request, CancellationToken ct = default)
    {
        var (text, fileName) = await ReadFileAsync(request, ct);
        var adapter = ResolveAdapter(request.Format, fileName);
        var originalHash = Sha256(text);

        // Từ đây trở đi mọi lối thoát đều phải để lại một dòng import_records.
        JsonObject content;
        try
        {
            content = adapter.Parse(text);
        }
        catch (ScenarioFormatException ex)
        {
            await RecordFailureAsync(adapter.Format, fileName, originalHash, text,
                new { stage = "parse", message = ex.Message }, ct);
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["file"] = [ex.Message]
            });
        }

        ApplySourceBlock(content, adapter.Format, fileName, originalHash);
        var (name, category) = ResolveProfile(content, request);

        var raw = JsonSerializer.SerializeToElement(content);
        var normalized = ScenarioContentNormalizer.Normalize(raw);
        var contentHash = hasher.ComputeHash(normalized);

        var mapName = ScenarioContentReader.AsString(normalized["map"]?["name"]);
        var maps = await uow.MapMetadata.ListActiveAsync(ct);
        var map = maps.FirstOrDefault(m => m.MapName == mapName);

        var result = await validator.ValidateAsync(new ScenarioValidationContext
        {
            Raw = raw,
            Normalized = normalized,
            MapName = mapName,
            Map = map,
            MapMetadataAvailable = maps.Count > 0
        }, ct);

        var report = BuildReport(content, normalized, result);

        if (!result.IsValid)
        {
            await RecordFailureAsync(adapter.Format, fileName, originalHash, text,
                new
                {
                    stage = "validate",
                    stopped_at = result.StoppedAt?.ToDbValue(),
                    errors = result.Errors.Select(e => new { tier = e.Tier.ToDbValue(), path = e.Path, message = e.Message })
                }, ct);

            throw new ImportValidationException(
                $"File '{fileName}' is invalid (stopped at the {result.StoppedAt?.ToDbValue()} tier). No scenario was created.",
                new
                {
                    stoppedAt = result.StoppedAt?.ToDbValue(),
                    errors = result.Errors.Select(e => new ValidationIssueResponse(e.Tier.ToDbValue(), e.Path, e.Message)).ToList(),
                    warnings = result.Warnings.Select(e => new ValidationIssueResponse(e.Tier.ToDbValue(), e.Path, e.Message)).ToList()
                });
        }

        return await CreateFromImportAsync(
            adapter.Format, fileName, originalHash, text,
            name, category, normalized, contentHash, map, result, report, ct);
    }

    public async Task<IReadOnlyList<ImportRecordResponse>> ListRecordsAsync(Guid scenarioId, CancellationToken ct = default)
    {
        _ = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        var records = await uow.ImportRecords.ListByScenarioAsync(scenarioId, ct);
        return records.Select(r => new ImportRecordResponse(
            r.Id, r.ScenarioId, r.Format, r.OriginalFileName, r.OriginalHash, r.Status, r.CreatedAt)).ToList();
    }

    // ------------------------------------------------------------------
    // Các bước con
    // ------------------------------------------------------------------
    private async Task<(string Text, string FileName)> ReadFileAsync(ImportScenarioRequest request, CancellationToken ct)
    {
        var file = request.File
            ?? throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["Choose a file to import."]
            });

        if (file.Length == 0)
            throw new BusinessRuleException(ErrorCodes.ImportFileEmpty, "The file is empty.");

        var maxBytes = (long)_options.MaxContentSizeKb * 1024;
        if (file.Length > maxBytes)
            throw new BusinessRuleException(ErrorCodes.ImportFileTooLarge,
                $"The file size ({file.Length / 1024} KB) exceeds the limit of {_options.MaxContentSizeKb} KB.");

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(text))
            throw new BusinessRuleException(ErrorCodes.ImportFileEmpty, "The file has no content.");

        var fileName = Path.GetFileName(file.FileName);
        return (text, string.IsNullOrWhiteSpace(fileName) ? "khong-ten" : fileName);
    }

    private IScenarioFormatAdapter ResolveAdapter(string? requested, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return formats.ByFormat(requested)
                ?? throw new BusinessRuleException(ErrorCodes.ImportFormatUnsupported,
                    ScenarioFormats.Planned.Contains(requested.Trim(), StringComparer.OrdinalIgnoreCase)
                        ? $"Format '{requested}' is planned but not yet supported."
                        : $"Format '{requested}' is not supported. Supported formats: {string.Join(", ", formats.SupportedFormats)}.");
        }

        return formats.ByFileName(fileName)
            ?? throw new BusinessRuleException(ErrorCodes.ImportFormatUnsupported,
                $"Unable to determine the format of file '{fileName}'. " +
                $"Use a registered extension (.json/.yaml/.yml/.xosc/.osc) or specify a format ({string.Join(", ", formats.SupportedFormats)}).");
    }

    /// <summary>
    /// Ghi đè khối source bằng thông tin import thật. Không tin khối source trong file gốc:
    /// nó có thể được chép từ nơi khác và nói dối về nguồn.
    /// </summary>
    private static void ApplySourceBlock(JsonObject content, string format, string fileName, string originalHash)
    {
        var existingWarnings = content["source"]?["import_warnings"]?.DeepClone();

        var source = new JsonObject
        {
            ["type"] = ScenarioOriginTypes.Import,
            ["format"] = format,
            ["original_file"] = fileName,
            ["original_hash"] = originalHash
        };
        if (existingWarnings is JsonArray warnings && warnings.Count > 0)
            source["import_warnings"] = warnings;

        content["source"] = source;
    }

    /// <summary>Tên và category của hồ sơ scenario: ưu tiên tham số gọi API, sau đó tới metadata trong file.</summary>
    private static (string Name, string Category) ResolveProfile(JsonObject content, ImportScenarioRequest request)
    {
        var metadata = content["metadata"] as JsonObject;

        var name = Pick(request.Name, ScenarioContentReader.AsString(metadata?["name"]));
        var category = Pick(request.Category, ScenarioContentReader.AsString(metadata?["category"]));

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name))
            errors["name"] = ["The file has no metadata.name. Provide a name."];
        if (string.IsNullOrWhiteSpace(category))
            errors["category"] = ["The file has no metadata.category. Provide a category."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        // Hồ sơ scenario và metadata trong nội dung phải khớp nhau, nếu không
        // danh sách repository hiện một đằng, file xuất ra một nẻo.
        metadata ??= [];
        metadata["name"] = name;
        metadata["category"] = category;
        content["metadata"] = metadata;

        return (name!, category!);

        static string? Pick(string? preferred, string? fallback)
            => string.IsNullOrWhiteSpace(preferred) ? fallback?.Trim() : preferred.Trim();
    }

    /// <summary>
    /// Báo cáo import: khối nào đọc được, trường nào phải điền mặc định, cảnh báo gì.
    /// "Điền mặc định" lấy từ diff giữa nội dung gốc và nội dung đã chuẩn hoá.
    /// </summary>
    private static ImportReportResponse BuildReport(
        JsonObject original, JsonObject normalized, ScenarioValidationResult result)
    {
        var mapped = original.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        var defaulted = JsonDiff.Compare(original, normalized)
            .Where(d => d.Change == JsonDiffChanges.Added)
            .Select(d => new ImportDefaultedFieldResponse(d.Path, d.After?.ToJsonString() ?? "null"))
            .ToList();

        var warnings = result.Warnings
            .Select(w => new ValidationIssueResponse(w.Tier.ToDbValue(), w.Path, w.Message))
            .ToList();

        return new ImportReportResponse(mapped, [], defaulted, warnings);
    }

    private async Task RecordFailureAsync(
        string format, string fileName, string originalHash, string text, object detail, CancellationToken ct)
    {
        uow.ImportRecords.Add(new ImportRecord
        {
            Id = Guid.NewGuid(),
            ScenarioId = null,                       // ck_import_scenario: FAILED thì phải NULL
            Format = format,
            OriginalFileName = fileName,
            OriginalHash = originalHash,
            OriginalContent = text,
            Status = ImportStatuses.Failed,
            ImportReport = JsonSerializer.Serialize(detail),
            CreatedBy = currentUser.UserId
        });
        await uow.SaveChangesAsync(ct);
    }

    private async Task<ScenarioImportResponse> CreateFromImportAsync(
        string format, string fileName, string originalHash, string text,
        string name, string category, JsonObject normalized, string contentHash,
        MapMetadata? map, ScenarioValidationResult result, ImportReportResponse report,
        CancellationToken ct)
    {
        var scenario = new Scenario
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = ScenarioContentReader.AsString(normalized["metadata"]?["description"]),
            Category = category,
            Status = ScenarioStatuses.Active,
            OriginType = ScenarioOriginTypes.Import,   // origin_version_id phải NULL (ck_scenarios_origin_parent)
            LatestVersionNumber = 0,
            CreatedBy = currentUser.UserId
        };

        var validationReport = new ValidationReport
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenario.Id,
            ContentHash = contentHash,
            IsValid = true,
            StoppedAt = null,
            Errors = result.Errors.ToJsonArray(),
            Warnings = result.Warnings.ToJsonArray(),
            SchemaVersion = ScenarioContentReader.AsString(normalized["schema_version"]) ?? ScenarioCatalog.CurrentSchemaVersion,
            ValidatorVersion = ValidatorVersion.Current,
            MapMetadataId = map?.Id,
            CreatedBy = currentUser.UserId
        };

        var importRecord = new ImportRecord
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenario.Id,
            Format = format,
            OriginalFileName = fileName,
            OriginalHash = originalHash,
            OriginalContent = text,
            Status = ImportStatuses.Succeeded,
            ImportReport = JsonSerializer.Serialize(report),
            CreatedBy = currentUser.UserId
        };

        // Tag của file (metadata.tags) thành tag mức scenario; nội dung draft vẫn giữ nguyên metadata.
        var tags = TagNames.FromContent(normalized["metadata"]?["tags"]);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.Scenarios.Add(scenario);
            await uow.Tags.AttachAsync(scenario.Id, tags, token);
            uow.ValidationReports.Add(validationReport);
            uow.ScenarioDrafts.Add(new ScenarioDraft
            {
                ScenarioId = scenario.Id,
                Content = normalized.ToJsonString(),
                LastValidationReportId = validationReport.Id,   // publish được ngay, không phải validate lại
                UpdatedBy = currentUser.UserId
            });
            uow.ImportRecords.Add(importRecord);
            uow.AuditLogs.Write(AuditEntityTypes.Import, importRecord.Id, AuditActions.Import, currentUser.UserId,
                new { scenario_id = scenario.Id, format, file = fileName, original_hash = originalHash, content_hash = contentHash });

            await uow.SaveChangesAsync(token);

            return new ScenarioImportResponse(
                importRecord.Id, format, fileName, originalHash,
                scenario.ToDetailResponse(latest: null, hasDraft: true, tags.Order(StringComparer.Ordinal).ToList()),
                report);
        }, ct);
    }

    private static string Sha256(string text)
        => $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()}";
}
