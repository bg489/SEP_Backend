namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

/// <summary>
/// Báo cáo một lần import: file gốc đã được hiểu tới đâu.
/// Lưu nguyên vào asx.import_records.import_report.
/// </summary>
/// <param name="Mapped">Các khối của schema v1.0 đọc được từ file gốc.</param>
/// <param name="Ignored">
/// Trường trong file gốc bị bỏ qua. Với JSON/YAML luôn rỗng vì schema đặt
/// additionalProperties = false nên trường lạ làm import thất bại ngay ở tầng 1;
/// danh sách này dành cho adapter CARLA Route XML / OpenSCENARIO sau này.
/// </param>
/// <param name="Defaulted">Trường thiếu trong file gốc, hệ thống tự điền giá trị mặc định theo schema.</param>
/// <param name="Warnings">Cảnh báo của validator (không chặn import).</param>
public sealed record ImportReportResponse(
    IReadOnlyList<string> Mapped,
    IReadOnlyList<string> Ignored,
    IReadOnlyList<ImportDefaultedFieldResponse> Defaulted,
    IReadOnlyList<ValidationIssueResponse> Warnings);

/// <summary>Một trường được điền mặc định khi import. Path là JSON Pointer.</summary>
public sealed record ImportDefaultedFieldResponse(string Path, string Value);

/// <summary>Kết quả POST /api/v1/scenarios/import.</summary>
public sealed record ScenarioImportResponse(
    Guid ImportRecordId,
    string Format,
    string OriginalFileName,
    string OriginalHash,
    ScenarioDetailResponse Scenario,
    ImportReportResponse Report);

/// <summary>Một dòng lịch sử import (asx.import_records).</summary>
public sealed record ImportRecordResponse(
    Guid Id,
    Guid? ScenarioId,
    string Format,
    string OriginalFileName,
    string OriginalHash,
    string Status,
    DateTimeOffset CreatedAt);
