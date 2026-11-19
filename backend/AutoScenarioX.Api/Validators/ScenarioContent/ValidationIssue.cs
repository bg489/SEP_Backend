namespace AutoScenarioX.Api.Validators.ScenarioContent;

public enum ValidationSeverity
{
    Error,
    Warning
}

/// <summary>
/// Một phát hiện của validator. Path là JSON Pointer (vd "/route/start")
/// để frontend đưa người dùng về đúng bước wizard bị lỗi.
/// </summary>
public sealed record ValidationIssue(
    ValidationTier Tier,
    ValidationSeverity Severity,
    string Path,
    string Message)
{
    public static ValidationIssue Error(ValidationTier tier, string path, string message)
        => new(tier, ValidationSeverity.Error, path, message);

    public static ValidationIssue Warning(ValidationTier tier, string path, string message)
        => new(tier, ValidationSeverity.Warning, path, message);
}
