using System.Globalization;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;

/// <summary>
/// Trigger theo thời gian mô phỏng (kể cả chuỗi after_event có gốc theo giờ) phải xảy ra TRƯỚC execution.timeout_s,
/// nếu không event không bao giờ chạy.
/// </summary>
public sealed class TriggerTimeoutRule : IScenarioValidationRule
{
    public ValidationTier Tier => ValidationTier.Business;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        var parameters = ScenarioContentReader.Parameters(context.Normalized);

        if (context.Normalized["execution"] is not JsonObject execution ||
            !ScenarioContentReader.TryGetNumber(execution["timeout_s"], parameters, out var timeout))
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var events = ScenarioContentReader.Events(context.Normalized);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not JsonObject evt) continue;
            if (evt["trigger"] is not JsonObject trigger) continue;
            var type = ScenarioContentReader.AsString(trigger["type"]);
            if (type is not ("simulation_time" or "after_event")) continue;
            if (ScenarioContentReader.FireTime(events, i, parameters) is not { } time) continue;

            if (time >= timeout)
                issues.Add(type == "simulation_time"
                    ? ValidationIssue.Error(Tier, $"/events/{i}/trigger/time_s",
                        string.Create(CultureInfo.InvariantCulture, $"The trigger fires at {time}s, but the simulation ends at {timeout}s. The event will never run."))
                    : ValidationIssue.Error(Tier, $"/events/{i}/trigger/delay_s",
                        string.Create(CultureInfo.InvariantCulture, $"The trigger fires at {time}s, counting the events it waits for, but the simulation ends at {timeout}s. The event will never run.")));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
