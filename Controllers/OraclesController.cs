using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/oracles/rules")]
public sealed class OraclesController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly List<OracleRuleResponse> Defaults =
    [
        new("collision", "Collision", "Detect vehicle or actor contact.", true, 0, "events"),
        new("lane-invasion", "Lane invasion", "Detect unintentional lane boundary crossings.", true, 0, "events"),
        new("unsafe-ttc", "Unsafe time to collision", "Minimum safe time-to-collision threshold.", true, 1.5, "seconds"),
        new("speeding", "Speed limit", "Maximum permitted speed for the scenario.", true, 50, "km/h"),
        new("traffic-light", "Traffic-light violation", "Detect red-light crossings.", true, 0, "events"),
        new("route-completion", "Route completion", "Minimum route progress required.", true, 95, "%"),
        new("stagnation", "Vehicle freezing", "Maximum stationary duration while route is incomplete.", true, 10, "seconds")
    ];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OracleRuleResponse>>> List(CancellationToken cancellationToken)
    {
        var rules = await repository.ListAsync<OracleRuleResponse>("oracle-rule", cancellationToken);
        return Ok(rules.Count == 0 ? Defaults : rules);
    }

    [HttpPut]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<IReadOnlyList<OracleRuleResponse>>> Save(List<OracleRuleResponse> rules, CancellationToken cancellationToken)
    {
        if (!rules.Any(rule => rule.Enabled) || rules.Any(rule => !double.IsFinite(rule.Threshold) || rule.Threshold < 0))
            return BadRequest(new MessageResponse("Enable at least one rule and use finite, non-negative thresholds."));
        if (rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != rules.Count)
            return BadRequest(new MessageResponse("Oracle rule IDs must be unique."));
        await repository.ReplaceAsync("oracle-rule", rules, rule => rule.Id, cancellationToken);
        await auditLog.WriteAsync(User, "Validate and activate oracle rules", $"{rules.Count} rule(s)", "Success", cancellationToken);
        return Ok(rules);
    }
}
