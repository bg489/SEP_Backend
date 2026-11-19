using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>
/// Actor bị event điều khiển không được để behavior = autopilot:
/// Traffic Manager và kịch bản sẽ tranh quyền điều khiển, kết quả chạy không tái lập được.
/// </summary>
public sealed class ScriptedActorAutopilotRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();

        var controlled = ScenarioContentReader.Events(context.Normalized)
            .OfType<JsonObject>()
            .Select(evt => ScenarioContentReader.AsString(evt["actor_ref"]))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        if (controlled.Count == 0) return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var actors = ScenarioContentReader.Actors(context.Normalized);
        for (var i = 0; i < actors.Count; i++)
        {
            if (actors[i] is not JsonObject actor) continue;
            if (ScenarioContentReader.AsString(actor["id"]) is not { } id || !controlled.Contains(id)) continue;
            if (ScenarioContentReader.AsString(actor["behavior"]) != "autopilot") continue;

            issues.Add(ValidationIssue.Error(Tier, $"/actors/{i}/behavior",
                $"Actor '{id}' is controlled by an event and cannot use 'autopilot' behavior. Use 'scripted' or 'idle'."));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
