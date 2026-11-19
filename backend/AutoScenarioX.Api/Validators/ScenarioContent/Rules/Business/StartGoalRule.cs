using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>Điểm xuất phát và đích phải khác nhau, nếu không ego "tới đích" ngay khi bắt đầu.</summary>
public sealed class StartGoalRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();

        if (context.Normalized["route"] is JsonObject route &&
            route["start"] is { } start && route["goal"] is { } goal &&
            ScenarioContentReader.AreEquivalent(start, goal))
        {
            issues.Add(ValidationIssue.Error(Tier, "/route/goal",
                "The goal matches the start position. Choose a different position."));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
