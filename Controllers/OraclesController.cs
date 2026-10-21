using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using AutoScenarioX.AuthApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/oracles/rules")]
public sealed class OraclesController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OracleRuleResponse>>> List(CancellationToken cancellationToken)
    {
        var rules = await repository.ListAsync<OracleRuleResponse>("oracle-rule", cancellationToken);
        return Ok(rules.Count == 0 ? OracleRuleCatalog.Defaults : rules);
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
