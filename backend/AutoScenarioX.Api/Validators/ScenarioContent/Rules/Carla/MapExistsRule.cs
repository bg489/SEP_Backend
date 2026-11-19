namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;

/// <summary>
/// Map khai báo trong scenario phải có trong asx.map_metadata.
/// Khi bảng metadata còn rỗng (chưa chạy bước nạp map từ CARLA) thì chỉ cảnh báo,
/// để Scenario Engineer vẫn làm việc được trên máy không cài CARLA.
/// </summary>
public sealed class MapExistsRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Carla;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();

        if (!context.MapMetadataAvailable)
        {
            issues.Add(ValidationIssue.Warning(Tier, "/map/name",
                "CARLA map metadata has not been loaded. Tier 3 checks (map, spawn points, positions, route and NPC count) were skipped."));
        }
        else if (context.Map is null && context.MapName is { } mapName)
        {
            issues.Add(ValidationIssue.Error(Tier, "/map/name",
                $"Map '{mapName}' is not in the map metadata loaded from CARLA."));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
