namespace AutoScenarioX.Api.DTOs.Prioritization;

public sealed record PrioritizationStrategyInfo(
    string Id,
    string Label,
    string Description);

public sealed record PrioritizationMetrics(
    double ApfdEstimate,
    double ApfdcEstimate,
    int FaultsFoundEarlyEstimate,
    int FaultsTotalEstimate,
    double EstimatedSuiteMinutes,
    string Note,
    string HistoryStatus = "no_data",
    int ObservedRuns = 0,
    double? ObservedApfd = null,
    double? ObservedApfdc = null);

public sealed record RankedScenarioItem(
    int Rank,
    Guid ScenarioVersionId,
    Guid ScenarioId,
    string ScenarioCode,
    string ScenarioName,
    string Category,
    string? MapName,
    string? WeatherPreset,
    string Severity,
    int FailureSignalEstimate,
    double EstimatedCostMinutes,
    double Score,
    string Rationale,
    int ObservedRuns = 0,
    int ObservedFailures = 0);

/// <summary>Ranked immutable versions; DistanceMatrix is normalized Euclidean distance, omitted above 40 candidates.</summary>
public sealed record PrioritizationRunResponse(
    Guid RunId,
    string Strategy,
    DateTimeOffset CreatedAt,
    PrioritizationMetrics Metrics,
    IReadOnlyList<RankedScenarioItem> Ordered,
    IReadOnlyList<string> FeatureNames,
    IReadOnlyList<IReadOnlyList<double>>? DistanceMatrix);
