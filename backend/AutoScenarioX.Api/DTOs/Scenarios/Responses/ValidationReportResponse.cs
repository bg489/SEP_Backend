namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

/// <summary>Một lỗi hoặc cảnh báo; Path là JSON Pointer để UI nhảy về đúng bước wizard.</summary>
public sealed record ValidationIssueResponse(string Tier, string Path, string Message);

/// <summary>Kết quả một lần validate bản nháp (asx.validation_reports).</summary>
public sealed record ValidationReportResponse(
    Guid Id,
    Guid ScenarioId,
    string ContentHash,
    bool IsValid,
    // StoppedAt: tầng dừng lại (schema | business | carla), null khi hợp lệ
    string? StoppedAt,
    IReadOnlyList<ValidationIssueResponse> Errors,
    IReadOnlyList<ValidationIssueResponse> Warnings,
    string SchemaVersion,
    string ValidatorVersion,
    DateTimeOffset CreatedAt,
    // Số version đã có cùng content_hash: nội dung hợp lệ nhưng publish sẽ bị từ chối (trùng)
    int? DuplicateOfVersionNumber = null);
