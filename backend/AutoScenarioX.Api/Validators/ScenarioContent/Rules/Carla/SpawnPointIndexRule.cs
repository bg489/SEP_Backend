namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;

/// <summary>Mọi spawn_point_index phải nằm trong số spawn point thật của map.</summary>
public sealed class SpawnPointIndexRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Carla;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        if (context.Map is not { } map) return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var parameters = ScenarioContentReader.Parameters(context.Normalized);

        foreach (var (path, node) in ScenarioContentReader.WalkObjects(context.Normalized))
        {
            if (!node.ContainsKey("spawn_point_index")) continue;
            if (!ScenarioContentReader.TryGetNumber(node["spawn_point_index"], parameters, out var index)) continue;

            if (index >= map.SpawnPointCount)
                issues.Add(ValidationIssue.Error(Tier, $"{path}/spawn_point_index",
                    $"Map '{map.MapName}' has {map.SpawnPointCount} spawn points (indices 0..{map.SpawnPointCount - 1}). Index {index} is out of range."));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
