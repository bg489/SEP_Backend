using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;

/// <summary>
/// Hai thứ không thể sinh ra chồng lên nhau trong CARLA (spawn báo va chạm và thất bại): mỗi actor
/// không được dùng cùng spawn point, hoặc cách dưới 1 m, với ego (route.start) hay một actor khai báo trước nó.
/// Mỗi actor chỉ báo xung đột đầu tiên.
/// </summary>
public sealed class SpawnOverlapRule : IScenarioValidationRule
{
    public const double MinSeparationM = 1;

    public ValidationTier Tier => ValidationTier.Carla;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        if (context.Map is not { } map) return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var points = ScenarioGeometry.SpawnPoints(map);
        var parameters = ScenarioContentReader.Parameters(context.Normalized);
        var placed = new List<(string Name, ScenarioPosition Position)>();
        if (context.Normalized["route"] is JsonObject route &&
            ScenarioGeometry.TryResolve(route["start"], parameters, points, out var start))
            placed.Add(("the ego vehicle", start));

        var actors = ScenarioContentReader.Actors(context.Normalized);
        for (var i = 0; i < actors.Count; i++)
        {
            if (actors[i] is not JsonObject actor ||
                !ScenarioGeometry.TryResolve(actor["spawn"], parameters, points, out var position))
                continue;
            var id = ScenarioContentReader.AsString(actor["id"]);

            foreach (var (other, at) in placed)
            {
                string? reason = null;
                if (position.SpawnIndex is { } index && at.SpawnIndex == index)
                    reason = $"both use spawn point #{index}";
                else if (ScenarioGeometry.Distance3D(position, at) < MinSeparationM)
                    reason = "they are less than 1 m apart";
                if (reason is null) continue;

                issues.Add(ValidationIssue.Error(Tier, $"/actors/{i}",
                    $"Actor '{id}' spawns on top of {other}: {reason}. CARLA cannot spawn overlapping actors."));
                break;
            }
            placed.Add(($"actor '{id}'", position));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
