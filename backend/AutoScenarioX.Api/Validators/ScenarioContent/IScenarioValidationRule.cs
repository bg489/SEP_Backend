namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>
/// Một luật validate. Thêm luật mới = tạo class cài đặt interface này rồi đăng ký DI
/// trong Extensions/ServiceCollectionExtensions.cs - KHÔNG sửa ScenarioValidator.
/// </summary>
public interface IScenarioValidationRule
{
    ValidationTier Tier { get; }

    Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default);
}
