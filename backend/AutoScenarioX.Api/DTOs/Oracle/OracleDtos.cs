using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Oracle;

public sealed record OracleConfigResponse(
    Guid Id, int Version, string Name, string? Description,
    decimal SpeedingToleranceKmh, decimal SpeedingMinDurationS, decimal FreezingMaxStillS,
    decimal TtcThresholdS, decimal RouteGoalToleranceM, DateTimeOffset CreatedAt);

public sealed record ViolationResponse(
    Guid Id, string Check, string Severity, decimal T, decimal? DurationS, JsonElement Details);

public sealed record EvaluationResponse(
    Guid Id, Guid OracleConfigId, int OracleVersion, string Verdict, string? ErrorReason,
    DateTimeOffset EvaluatedAt, IReadOnlyList<ViolationResponse> Violations);

/// <summary>Worker claim một run từ hàng đợi (Queued → Claimed).</summary>
public sealed record ClaimedRunResponse(
    Guid RunId, Guid CampaignId, string? CampaignCode, string CampaignName,
    Guid ScenarioVersionId, string? ContentHash, int? Seed, int SortOrder,
    string SutType, string SutBehavior, string SutCarlaVersion,
    int InfraRetryCount, int MaxInfraRetries);

public sealed record RequeueRunResponse(
    Guid RunId, int InfraRetryCount, int MaxInfraRetries, string Status, Guid? PreviousRunId = null);

public sealed record RunProgressRequest(
    double SimulationTimeS, long Frame, double EgoX, double EgoY, double EgoZ,
    double SpeedKmh, string? Message = null);

public sealed record RunProgressResponse(Guid RunId, string Status, DateTimeOffset? UpdatedAt, RunProgressRequest? Progress);

/// <summary>
/// Kết quả Oracle của một simulation run. Verdict = theo Oracle Config campaign ghim (null khi chưa chấm);
/// Evaluations gồm mọi lần chấm, mới nhất trước.
/// </summary>
public sealed record RunResultResponse(
    Guid RunId, Guid CampaignId, string? CampaignCode, string CampaignName,
    Guid ScenarioId, Guid ScenarioVersionId, string ScenarioCode, string ScenarioName, int VersionNumber,
    string Status, int? Seed, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    string? Outcome, decimal? DurationS, DateTimeOffset? TelemetryReceivedAt,
    string? Verdict, bool HasVideo, int InfraRetryCount,
    IReadOnlyList<EvaluationResponse> Evaluations);
