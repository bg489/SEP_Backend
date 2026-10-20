namespace AutoScenarioX.AuthApi.Contracts;

public sealed record ScenarioResponse(
    string Id,
    string Name,
    string Description,
    string Map,
    string Category,
    string Severity,
    string Status,
    string Version,
    string Weather,
    int Actors,
    int RunCount,
    double PassRate,
    string LastRun,
    string Owner,
    List<string> Tags,
    int PriorityScore,
    string? RouteDescription = null,
    string? EgoVehicle = null,
    string? TrafficDensity = null,
    int? PedestrianCount = null,
    int? ObstacleCount = null,
    string? TriggerCondition = null,
    string? Actions = null,
    string? SourceFileName = null);

public sealed record CreateScenarioRequest(
    string Name,
    string Description,
    string Map,
    string Category,
    string Severity,
    string Weather);

public sealed record ScenarioVersionResponse(string Version, string SavedAt, string Note, string Hash, string Status);
public sealed record ScenarioStatusRequest(string Status, string Note = "");
public sealed record ExecutionMetricsRequest(int RunCount, double PassRate, string LastRun);

public sealed record CampaignResponse(
    string Id,
    string Name,
    string Description,
    List<string> ScenarioIds,
    string Strategy,
    string Status,
    int Progress,
    string Owner,
    string CreatedAt,
    double PassRate,
    int FailedRuns,
    string EstimatedDuration,
    string? SutName = null,
    string? SutVersion = null,
    string? Agent = null,
    int? RandomSeed = null,
    int? Repetitions = null,
    int? TimeoutSeconds = null,
    string? OracleProfile = null);

public sealed record CreateCampaignRequest(
    string Name,
    string Description,
    List<string> ScenarioIds,
    string Strategy,
    string? SutName = null,
    string? SutVersion = null,
    string? Agent = null,
    int? RandomSeed = null,
    int? Repetitions = null,
    int? TimeoutSeconds = null,
    string? OracleProfile = null);

public sealed record FailureEvidenceResponse(string Timestamp, string Rule, string Measured, string Threshold, string Note);
public sealed record FailureClusterResponse(
    string Id,
    string Label,
    string RootCause,
    string ReviewStatus,
    double Similarity,
    int AffectedRuns,
    string LastDetected,
    string Map,
    string ScenarioName,
    double Confidence,
    List<FailureEvidenceResponse> Evidence,
    string? SourceRunId = null,
    string? ReviewedBy = null,
    string? ReviewerNote = null,
    string? OriginalLabel = null);

public sealed record WorkerNodeResponse(
    string Id,
    string Name,
    string Host,
    int Port,
    string CarlaVersion,
    string ScenarioRunnerVersion,
    string Status,
    int Capacity,
    int ActiveRuns,
    string LastHeartbeat);

public sealed record OracleRuleResponse(string Id, string Name, string Description, bool Enabled, double Threshold, string Unit);

public sealed record ScenarioRunResponse(
    string Id,
    string CampaignId,
    string ScenarioId,
    string ScenarioName,
    string ScenarioVersion,
    string State,
    int Progress,
    string? Verdict,
    string? StartedAt,
    int? DurationSeconds,
    string? WorkerId,
    string? RetryOf,
    int? Repetition,
    string? SutVersion,
    string? Agent,
    int? RandomSeed,
    string? OracleProfile,
    List<string> Artifacts,
    List<FailureEvidenceResponse> Evidence);

public sealed record QueueCampaignRunsRequest(List<string>? ScenarioIds = null, string? RetryOf = null);
public sealed record OverviewResponse(
    int TotalScenarios,
    int ActiveCampaigns,
    double PassRate,
    int FailureClusters,
    List<int> WeeklyRuns,
    List<string> WeeklyLabels,
    List<RecentActivityResponse> RecentActivity);
public sealed record RecentActivityResponse(string Id, string Title, string Detail, string Time, string Type);
public sealed record CarlaIntegrationResponse(
    string Host,
    int RpcPort,
    int StreamingPort,
    string CarlaVersion,
    string ScenarioRunnerPath,
    string QualityLevel,
    bool Validated,
    bool Active,
    string ConnectionStatus,
    string UpdatedAt);

public sealed record FeatureWeightsResponse(double Map, double Category, double Weather, double Severity, double Actors);
public sealed record AnalyticsPreferencesResponse(
    double SimilarityWarning,
    double SimilarityBlock,
    FeatureWeightsResponse FeatureWeights,
    int RetentionDays,
    string ReportDays,
    bool IncludeDrafts,
    string Group,
    bool WeeklyEmail);
public sealed record AuditEventResponse(string Id, string Time, string Actor, string Action, string Target, string Result);
