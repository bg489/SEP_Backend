using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/analytics/preferences")]
public sealed class AnalyticsSettingsController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly AnalyticsPreferencesResponse Defaults = new(
        .78, .93, new FeatureWeightsResponse(1, 1, 1, 1, 1), 180, "30", false, "severity", true);

    [HttpGet]
    public async Task<ActionResult<AnalyticsPreferencesResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await repository.GetAsync<AnalyticsPreferencesResponse>("analytics", "preferences", cancellationToken) ?? Defaults);

    [HttpPut]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<AnalyticsPreferencesResponse>> Save(AnalyticsPreferencesResponse preferences, CancellationToken cancellationToken)
    {
        if (!(preferences.SimilarityWarning >= 0 && preferences.SimilarityWarning < preferences.SimilarityBlock && preferences.SimilarityBlock <= 1))
            return BadRequest(new MessageResponse("Similarity thresholds must satisfy 0 ≤ warning < block ≤ 1."));
        var weights = preferences.FeatureWeights;
        var values = new[] { weights.Map, weights.Category, weights.Weather, weights.Severity, weights.Actors };
        if (values.Any(value => !double.IsFinite(value) || value < 0) || values.All(value => value == 0))
            return BadRequest(new MessageResponse("Feature weights must be non-negative and at least one must be above zero."));
        if (preferences.RetentionDays < 1 || !int.TryParse(preferences.ReportDays, out var days) || days < 1)
            return BadRequest(new MessageResponse("Retention and reporting periods must be positive numbers of days."));
        await repository.SaveAsync("analytics", "preferences", preferences, cancellationToken);
        await auditLog.WriteAsync(User, "Update analytics and similarity settings", "Workspace analytics", "Success", cancellationToken);
        return Ok(preferences);
    }
}
