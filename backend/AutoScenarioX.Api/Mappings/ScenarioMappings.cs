using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Reference;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Mappings;

/// <summary>Chuyển Entity -> Response DTO. Entity không bao giờ được trả thẳng ra API.</summary>
public static class ScenarioMappings
{
    public static ScenarioListItemResponse ToListItemResponse(this ScenarioRepositoryItem x) => new(
        x.Id, x.Code, x.Name, x.Category, x.Status,
        x.LatestVersionNumber, x.LatestVersionStatus,
        x.MapName, x.WeatherPreset, JsonHelper.ToStringArray(x.EventTypes),
        x.HasDraft, x.Tags, x.UpdatedAt);

    public static ScenarioVersionSummaryResponse ToSummaryResponse(this ScenarioVersion v) => new(
        v.Id, v.VersionNumber, v.Status, v.ContentHash,
        v.MapName, v.WeatherPreset, JsonHelper.ToStringArray(v.EventTypes),
        v.ActorCount, v.EventCount, v.SourceType, v.ChangeNote, v.PublishedAt);

    public static ScenarioVersionDetailResponse ToDetailResponse(this ScenarioVersion v) => new(
        v.ToSummaryResponse(), v.ScenarioId, v.SchemaVersion,
        v.NpcVehicleCount, v.PedestrianCount, v.SpeedLimitKmh, v.EgoBlueprint,
        v.ParentVersionId, v.ValidationReportId, JsonHelper.ToElement(v.Content));

    public static ScenarioDetailResponse ToDetailResponse(
        this Scenario s, ScenarioVersion? latest, bool hasDraft, IReadOnlyList<string> tags) => new(
        s.Id, s.Code, s.Name, s.Description, s.Category, s.Status, s.OriginType, hasDraft, tags,
        s.CreatedAt, s.UpdatedAt,
        latest?.ToSummaryResponse(),
        latest is null ? null : JsonHelper.ToElement(latest.Content));

    public static ScenarioDraftResponse ToResponse(this ScenarioDraft d) => new(
        d.ScenarioId, d.BaseVersionId, d.LastValidationReportId, d.RowVersion, d.UpdatedAt,
        JsonHelper.ToElement(d.Content));

    public static MapResponse ToResponse(this MapMetadata m) => new(
        m.MapName, m.CarlaVersion, m.SpawnPointCount, m.PreviewImagePath);
}
