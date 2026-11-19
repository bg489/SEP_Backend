using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

public sealed record ScenarioDetailResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Category,
    string Status,
    string OriginType,
    bool HasDraft,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ScenarioVersionSummaryResponse? LatestVersion,
    JsonElement? LatestContent);
