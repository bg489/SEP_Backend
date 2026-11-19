namespace AutoScenarioX.Api.DTOs.Campaigns;

public sealed class CreateCampaignRequest
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string Strategy { get; init; } = "proposed_failure_history";
    /// <summary>Danh sách scenario_version_id READY (thứ tự = thứ tự chạy).</summary>
    public IReadOnlyList<Guid> ScenarioVersionIds { get; init; } = [];
    public string SutType { get; init; } = "behavior_agent";
    public string SutBehavior { get; init; } = "normal";
    public string SutCarlaVersion { get; init; } = "0.9.16";
    public Guid? OracleConfigId { get; init; }
}

public sealed class UpdateCampaignRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Strategy { get; init; }
    public string? SutType { get; init; }
    public string? SutBehavior { get; init; }
    public string? SutCarlaVersion { get; init; }
    public Guid? OracleConfigId { get; init; }
    /// <summary>Nếu có: thay thứ tự / danh sách version (chỉ khi campaign còn Queued/Draft).</summary>
    public IReadOnlyList<Guid>? ScenarioVersionIds { get; init; }
}

public sealed record CampaignScenarioItem(
    Guid ScenarioVersionId,
    Guid ScenarioId,
    string ScenarioCode,
    string ScenarioName,
    string Category,
    int VersionNumber,
    string? MapName,
    string? WeatherPreset,
    int SortOrder);

public sealed record CampaignSutConfig(
    string Type,
    string Behavior,
    string CarlaVersion);

public sealed record CampaignResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Strategy,
    string Status,
    int Progress,
    decimal PassRate,
    int FailedRuns,
    string EstimatedDuration,
    string Owner,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    CampaignSutConfig Sut,
    IReadOnlyList<Guid> ScenarioVersionIds,
    IReadOnlyList<CampaignScenarioItem> Scenarios,
    int RunCount,
    Guid? OracleConfigId = null);

public sealed record SimulationRunResponse(
    Guid Id,
    Guid CampaignId,
    Guid ScenarioVersionId,
    Guid ScenarioId,
    string ScenarioCode,
    string ScenarioName,
    string Category,
    int VersionNumber,
    string? MapName,
    string? WeatherPreset,
    int SortOrder,
    string Status,
    string? Verdict,
    string? ContentHash,
    int? Seed,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorMessage,
    int Attempt = 1,
    Guid? PreviousRunId = null,
    bool IsCurrent = true,
    Guid? WorkerId = null);
