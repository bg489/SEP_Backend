using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>
/// actor_ref của event và của trigger phải trỏ tới actor có thật,
/// và actor phải đúng loại mà event yêu cầu (pedestrian_crossing cần pedestrian...).
/// </summary>
public sealed class ActorReferenceRule : IScenarioValidationRule
{
    /// <summary>Loại actor bắt buộc cho từng event type. traffic_light_override không dùng actor.</summary>
    private static readonly Dictionary<string, string> RequiredActorType = new(StringComparer.Ordinal)
    {
        ["pedestrian_crossing"] = "pedestrian",
        ["vehicle_cut_in"] = "vehicle",
        ["lead_vehicle_brake"] = "vehicle",
        ["vehicle_crossing"] = "vehicle"
    };

    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        var actorTypes = ScenarioContentReader.ActorTypesById(context.Normalized);
        var events = ScenarioContentReader.Events(context.Normalized);

        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not JsonObject evt) continue;
            var eventType = ScenarioContentReader.AsString(evt["type"]);

            if (ScenarioContentReader.AsString(evt["actor_ref"]) is { } actorRef)
            {
                var path = $"/events/{i}/actor_ref";
                if (!actorTypes.TryGetValue(actorRef, out var actorType))
                {
                    issues.Add(ValidationIssue.Error(Tier, path,
                        $"The event references actor '{actorRef}', which is not defined in actors."));
                }
                else if (eventType is not null &&
                         RequiredActorType.TryGetValue(eventType, out var required) &&
                         actorType != required)
                {
                    issues.Add(ValidationIssue.Error(Tier, path,
                        $"Event '{eventType}' requires an actor of type '{required}', but '{actorRef}' is '{actorType}'."));
                }
            }

            if (evt["trigger"] is JsonObject trigger &&
                ScenarioContentReader.AsString(trigger["actor_ref"]) is { } triggerRef &&
                !actorTypes.ContainsKey(triggerRef))
            {
                issues.Add(ValidationIssue.Error(Tier, $"/events/{i}/trigger/actor_ref",
                    $"The trigger references actor '{triggerRef}', which is not defined in actors."));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
