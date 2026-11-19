using System.Globalization;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>Giá trị hiện tại của mỗi tham số phải nằm trong [min, max] và min không lớn hơn max.</summary>
public sealed class ParameterRangeRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        var parameters = ScenarioContentReader.Parameters(context.Normalized);
        var empty = new JsonObject();

        foreach (var (name, node) in parameters)
        {
            if (node is not JsonObject definition) continue;

            var path = $"/parameters/{name}";
            if (!ScenarioContentReader.TryGetNumber(definition["value"], empty, out var value) ||
                !ScenarioContentReader.TryGetNumber(definition["min"], empty, out var min) ||
                !ScenarioContentReader.TryGetNumber(definition["max"], empty, out var max))
                continue;   // thiếu / sai kiểu đã bị tầng 1 bắt

            if (min > max)
            {
                issues.Add(ValidationIssue.Error(Tier, path, string.Create(CultureInfo.InvariantCulture, $"Parameter '{name}' has a minimum ({min}) greater than its maximum ({max}).")));
                continue;
            }

            if (value < min || value > max)
                issues.Add(ValidationIssue.Error(Tier, $"{path}/value",
                    string.Create(CultureInfo.InvariantCulture, $"Parameter '{name}' has value {value}, outside the range [{min}, {max}].")));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
