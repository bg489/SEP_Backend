namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>Mọi tham chiếu {"$param": "ten"} phải trỏ tới một tham số đã khai báo trong khối parameters.</summary>
public sealed class ParamReferenceRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        var parameters = ScenarioContentReader.Parameters(context.Normalized);

        foreach (var (path, node) in ScenarioContentReader.WalkObjects(context.Normalized))
        {
            if (ScenarioContentReader.AsParamRef(node) is not { } name) continue;
            if (parameters.ContainsKey(name)) continue;

            issues.Add(ValidationIssue.Error(Tier, path,
                $"Parameter reference '{name}' is not defined in parameters."));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
