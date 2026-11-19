namespace AutoScenarioX.Api.DTOs.FailureAnalysis;

public sealed record FailureAnalysisResponse(
    Guid AnalysisId,
    Guid CampaignId,
    string CampaignCode,
    string CampaignName,
    string Step,
    int RunsJudged,
    int PassCount,
    int FailCount,
    int ErrorExcluded,
    int PendingCount,
    string Summary,
    string? ReviewNote,
    IReadOnlyList<RunFeatureResponse> Features,
    IReadOnlyList<FaultGroupResponse> Faults,
    EffectivenessResponse? Effectiveness);

public sealed record ReviewFailureReportRequest(string Decision, string? Note);

public sealed record RunFeatureResponse(
    Guid RunId,
    int SortOrder,
    string ScenarioCode,
    string ScenarioName,
    string Verdict,
    string? Outcome,
    double? DurationS,
    bool HasVideo,
    IReadOnlyList<string> FailingChecks,
    IReadOnlyList<string> WarningChecks);

public sealed record FaultGroupResponse(
    Guid RootScenarioId,
    string RootScenarioCode,
    string RootScenarioName,
    string Check,
    IReadOnlyList<Guid> RunIds,
    Guid FirstDetectedRunId,
    double FirstT,
    string? Explanation,
    string? Report,
    bool NeedsAdditionalVerification,
    string Status,
    IReadOnlyList<Guid> SimilarRunIds);

public sealed record EffectivenessResponse(
    OrderEffectivenessResponse Proposed,
    OrderEffectivenessResponse ReverseBaseline,
    OrderEffectivenessResponse RandomBaseline,
    double? ApfdGainVsReverse);

public sealed record OrderEffectivenessResponse(
    string Label,
    double? Apfd,
    double? Apfdc,
    IReadOnlyList<DetectionPointResponse> Curve);

public sealed record DetectionPointResponse(int TestsRun, int FaultsFound, double Fraction);
