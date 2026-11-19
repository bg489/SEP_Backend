namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

public sealed record ScenarioListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string Category,
    string Status,
    int LatestVersionNumber,
    string? LatestVersionStatus,
    string? MapName,
    string? WeatherPreset,
    string[] EventTypes,
    bool HasDraft,
    string[] Tags,
    DateTimeOffset UpdatedAt);
