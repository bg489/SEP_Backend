using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

/// <summary>Version đầy đủ, gồm toàn bộ cấu hình (map, route, weather, actors, events...).</summary>
public sealed record ScenarioVersionDetailResponse(
    ScenarioVersionSummaryResponse Summary,
    Guid ScenarioId,
    string SchemaVersion,
    int? NpcVehicleCount,
    int? PedestrianCount,
    decimal? SpeedLimitKmh,
    string? EgoBlueprint,
    Guid? ParentVersionId,
    Guid ValidationReportId,
    JsonElement? Content);
