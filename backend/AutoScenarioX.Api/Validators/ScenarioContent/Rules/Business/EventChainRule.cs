using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>
/// Trigger after_event phải chờ một event có thật, và chuỗi chờ không được vòng lại chính nó
/// (A chờ B, B chờ A – hoặc A chờ A), nếu không các event trong vòng không bao giờ bắt đầu.
/// </summary>
public sealed class EventChainRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        var events = ScenarioContentReader.Events(context.Normalized);

        for (var i = 0; i < events.Count; i++)
        {
            if (WaitsFor(events, i) is not { } eventRef) continue;
            var path = $"/events/{i}/trigger/event_ref";
            if (ScenarioContentReader.IndexOfEvent(events, eventRef) < 0)
                issues.Add(ValidationIssue.Error(Tier, path,
                    $"The trigger waits for event '{eventRef}', which is not defined in events."));
            else if (InLoop(events, i))
                issues.Add(ValidationIssue.Error(Tier, path,
                    $"Event '{ScenarioContentReader.AsString(events[i]!["id"])}' waits for itself through after_event triggers, so it never starts."));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private static string? WaitsFor(JsonArray events, int index)
        => events[index] is JsonObject { } evt && evt["trigger"] is JsonObject trigger &&
           ScenarioContentReader.AsString(trigger["type"]) == "after_event"
            ? ScenarioContentReader.AsString(trigger["event_ref"])
            : null;

    /// <summary>Đi theo chuỗi chờ từ start: quay lại đúng start thì start nằm trong vòng.</summary>
    private static bool InLoop(JsonArray events, int start)
    {
        var seen = new HashSet<int>();
        for (var j = start; seen.Add(j);)
        {
            j = WaitsFor(events, j) is { } next ? ScenarioContentReader.IndexOfEvent(events, next) : -1;
            if (j == start) return true;
            if (j < 0) return false;
        }
        return false;
    }
}
