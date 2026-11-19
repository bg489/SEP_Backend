using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Mappings;

public static class OracleMappings
{
    public static OracleConfigResponse ToResponse(this OracleConfig c) => new(
        c.Id, c.Version, c.Name, c.Description, c.SpeedingToleranceKmh, c.SpeedingMinDurationS,
        c.FreezingMaxStillS, c.TtcThresholdS, c.RouteGoalToleranceM, c.CreatedAt);

    public static EvaluationResponse ToResponse(this Evaluation e) => new(
        e.Id, e.OracleConfigId, e.OracleConfig.Version, e.Verdict, e.ErrorReason, e.EvaluatedAt,
        e.Violations.OrderBy(v => v.T).ThenBy(v => v.CheckName)
            .Select(v => new ViolationResponse(v.Id, v.CheckName, v.Severity, v.T, v.DurationS, JsonHelper.ToElement(v.Details)!.Value))
            .ToList());

    public static RunResultResponse ToResult(this SimulationRun r, Scenario s, ScenarioVersion v) => new(
        r.Id, r.CampaignId, r.Campaign.Code, r.Campaign.Name, s.Id, v.Id, s.Code, s.Name, v.VersionNumber,
        r.Status, r.Seed, r.StartedAt, r.FinishedAt,
        r.Telemetry?.Outcome, r.Telemetry?.DurationS, r.Telemetry?.ReceivedAt,
        r.Evaluations.FirstOrDefault(e => e.OracleConfigId == r.Campaign.OracleConfigId)?.Verdict,
        !string.IsNullOrEmpty(r.Telemetry?.VideoPath),
        r.InfraRetryCount,
        r.Evaluations.OrderByDescending(e => e.EvaluatedAt).Select(e => e.ToResponse()).ToList());
}
