using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;

/// <summary>
/// Số xe NPC không được nhiều hơn số spawn point còn trống
/// (trừ điểm của ego và các actor đã đặt cố định theo spawn point).
/// </summary>
public sealed class NpcCountRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Carla;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        if (context.Map is not { } map) return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        if (context.Normalized["traffic"] is not JsonObject traffic ||
            traffic["npc_vehicles"] is not JsonObject npc)
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var parameters = ScenarioContentReader.Parameters(context.Normalized);
        if (!ScenarioContentReader.TryGetNumber(npc["count"], parameters, out var count))
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var occupiedByActors = ScenarioContentReader.Actors(context.Normalized)
            .OfType<JsonObject>()
            .Count(actor => actor["spawn"] is JsonObject spawn && spawn.ContainsKey("spawn_point_index"));

        var available = map.SpawnPointCount - 1 - occupiedByActors;   // -1 cho ego
        if (count > available)
            issues.Add(ValidationIssue.Error(Tier, "/traffic/npc_vehicles/count",
                $"Map '{map.MapName}' has {available} free spawn points ({map.SpawnPointCount} total, excluding the ego vehicle and {occupiedByActors} actors), insufficient for {count} NPC vehicles."));

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
