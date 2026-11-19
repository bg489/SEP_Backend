namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>
/// Chạy các luật theo đúng thứ tự Schema -> Business -> Carla.
/// DỪNG ngay khi một tầng có lỗi (tầng sau chỉ chạy khi tầng trước sạch lỗi),
/// vì kết quả của tầng sau vô nghĩa khi cấu trúc dữ liệu còn sai.
/// </summary>
public sealed class ScenarioValidator(IEnumerable<IScenarioValidationRule> rules) : IScenarioValidator
{
    private static readonly ValidationTier[] TierOrder =
        [ValidationTier.Schema, ValidationTier.Business, ValidationTier.Carla];

    public async Task<ScenarioValidationResult> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var warnings = new List<ValidationIssue>();

        foreach (var tier in TierOrder)
        {
            var issues = new List<ValidationIssue>();
            foreach (var rule in rules.Where(r => r.Tier == tier))
                issues.AddRange(await rule.ValidateAsync(context, ct));

            warnings.AddRange(issues.Where(i => i.Severity == ValidationSeverity.Warning));

            var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
            if (errors.Count > 0)
                return new ScenarioValidationResult(false, tier, errors, warnings);
        }

        return new ScenarioValidationResult(true, null, [], warnings);
    }
}
