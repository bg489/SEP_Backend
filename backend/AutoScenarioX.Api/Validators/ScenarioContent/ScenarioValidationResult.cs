namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>
/// Kết quả validate. IsValid = true khi không có lỗi nào (warning KHÔNG làm hỏng kết quả,
/// khớp CHECK ck_validation_consistency trong DB).
/// </summary>
public sealed record ScenarioValidationResult(
    bool IsValid,
    ValidationTier? StoppedAt,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings);
