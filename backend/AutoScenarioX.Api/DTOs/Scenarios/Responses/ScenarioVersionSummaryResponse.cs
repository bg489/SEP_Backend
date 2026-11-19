namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

public sealed record ScenarioVersionSummaryResponse(
    Guid Id,
    int VersionNumber,
    string Status,
    string ContentHash,
    string? MapName,
    string? WeatherPreset,
    string[] EventTypes,
    int ActorCount,
    int EventCount,
    string SourceType,
    string? ChangeNote,
    DateTimeOffset PublishedAt);
