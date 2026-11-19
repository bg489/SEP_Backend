using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Nạp scenario từ file ngoài vào hệ thống (B11).</summary>
public interface IScenarioImportService
{
    /// <summary>
    /// Đọc file, chuyển sang JSON nội bộ, validate 3 tầng rồi tạo scenario mới kèm bản nháp.
    /// Mọi lần import đều ghi một dòng asx.import_records, kể cả khi thất bại.
    /// Nội dung không hợp lệ thì ném BusinessRuleException (422) kèm report.
    /// </summary>
    Task<ScenarioImportResponse> ImportAsync(ImportScenarioRequest request, CancellationToken ct = default);

    /// <summary>Lịch sử import của một scenario.</summary>
    Task<IReadOnlyList<ImportRecordResponse>> ListRecordsAsync(Guid scenarioId, CancellationToken ct = default);
}

/// <summary>Xuất scenario ra file để chia sẻ hoặc nạp vào hệ thống khác (B11).</summary>
public interface IScenarioExportService
{
    /// <summary>Xuất nội dung một version đã publish.</summary>
    Task<ScenarioExportResult> ExportVersionAsync(
        Guid scenarioId, int versionNumber, string? format, CancellationToken ct = default);

    /// <summary>Xuất nội dung bản nháp đang soạn.</summary>
    Task<ScenarioExportResult> ExportDraftAsync(Guid scenarioId, string? format, CancellationToken ct = default);
}

/// <summary>File kết quả của một lần export.</summary>
public sealed record ScenarioExportResult(string FileName, string MediaType, string Content);
