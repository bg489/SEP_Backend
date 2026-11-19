using System.Globalization;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.DTOs.Handoff;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;

/// <summary>
/// Trigger theo khoảng cách (ego_distance_to_actor / ego_distance_to_location) đã thoả ngay tại vị trí xuất phát
/// của ego thì event chạy từ giây đầu tiên, thường không phải ý đồ của kịch bản: CẢNH BÁO.
/// Đo 3D như điều kiện trigger của CARLA (Location.distance).
/// Trigger time_to_collision: ego xuất phát với initial_speed_kmh &gt; 0 mà khoảng cách tới actor ÷ tốc độ đó đã
/// ≤ ttc_s thì cũng cảnh báo (coi như ego đang tiến thẳng về actor – cận trên của tốc độ tiếp cận).
/// </summary>
public sealed class TriggerAtStartRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Carla;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        if (context.Map is not { } map || context.Normalized["route"] is not JsonObject route)
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var points = ScenarioGeometry.SpawnPoints(map);
        var parameters = ScenarioContentReader.Parameters(context.Normalized);
        if (!ScenarioGeometry.TryResolve(route["start"], parameters, points, out var start))
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var actors = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var node in ScenarioContentReader.Actors(context.Normalized))
            if (node is JsonObject actor && ScenarioContentReader.AsString(actor["id"]) is { } id)
                actors.TryAdd(id, actor["spawn"]);

        var egoSpeed = context.Normalized["ego_vehicle"] is JsonObject ego &&
            ScenarioContentReader.TryGetNumber(ego["initial_speed_kmh"], parameters, out var speed) ? speed : 0;
        var events = ScenarioContentReader.Events(context.Normalized);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not JsonObject evt || evt["trigger"] is not JsonObject trigger) continue;
            if (ScenarioContentReader.AsString(trigger["type"]) == "time_to_collision")
            {
                CheckTimeToCollision(evt, trigger, i, egoSpeed, start, actors, parameters, points, issues);
                continue;
            }
            if (!ScenarioContentReader.TryGetNumber(trigger["distance_m"], parameters, out var threshold)) continue;

            var (target, label) = ScenarioContentReader.AsString(trigger["type"]) switch
            {
                "ego_distance_to_actor" when ScenarioContentReader.AsString(trigger["actor_ref"]) is { } actorRef
                    => (actors.GetValueOrDefault(actorRef), $"actor '{actorRef}'"),
                "ego_distance_to_location" => (trigger["location"], "the trigger location"),
                _ => (null, "")
            };
            if (!ScenarioGeometry.TryResolve(target, parameters, points, out var position)) continue;

            var distance = ScenarioGeometry.Distance3D(start, position);
            if (distance <= threshold)
                issues.Add(ValidationIssue.Warning(Tier, $"/events/{i}/trigger/distance_m", string.Create(CultureInfo.InvariantCulture,
                    $"The ego starts {ScenarioGeometry.Round1(distance)} m from {label}, within the {threshold} m trigger distance, so event '{ScenarioContentReader.AsString(evt["id"])}' fires as soon as the run starts.")));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private void CheckTimeToCollision(JsonObject evt, JsonObject trigger, int index, double egoSpeed,
        ScenarioPosition start, Dictionary<string, JsonNode?> actors, JsonObject parameters,
        IReadOnlyDictionary<int, SpawnTransform> points, List<ValidationIssue> issues)
    {
        if (egoSpeed <= 0 || !ScenarioContentReader.TryGetNumber(trigger["ttc_s"], parameters, out var threshold)) return;
        if (ScenarioContentReader.AsString(trigger["actor_ref"]) is not { } actorRef ||
            !ScenarioGeometry.TryResolve(actors.GetValueOrDefault(actorRef), parameters, points, out var position)) return;

        var distance = ScenarioGeometry.Distance3D(start, position);
        var ttc = distance / (egoSpeed / 3.6);
        if (ttc <= threshold)
            issues.Add(ValidationIssue.Warning(Tier, $"/events/{index}/trigger/ttc_s", string.Create(CultureInfo.InvariantCulture,
                $"The ego starts {ScenarioGeometry.Round1(distance)} m from actor '{actorRef}' at {egoSpeed} km/h, {ScenarioGeometry.Round1(ttc)} s away, within the {threshold} s trigger time, so event '{ScenarioContentReader.AsString(evt["id"])}' fires as soon as the run starts.")));
    }
}
