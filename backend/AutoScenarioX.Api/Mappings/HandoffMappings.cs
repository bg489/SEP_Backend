using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Handoff;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Handoff;

namespace AutoScenarioX.Api.Mappings;

public static class HandoffMappings
{
    public const string ExecutionPackageFormat = "asx.execution-package/1";

    public static ReadyScenarioVersionResponse ToResponse(this ReadyScenarioVersionItem x) => new(
        x.ScenarioVersionId, x.ScenarioId, x.Code, x.Name, x.Category, x.VersionNumber, x.IsLatestReady,
        x.ContentHash, x.MapName, x.WeatherPreset, JsonHelper.ToStringArray(x.EventTypes), x.EventCount,
        x.ActorCount, x.NpcVehicleCount, x.PedestrianCount, x.SpeedLimitKmh, x.EgoBlueprint, x.PublishedAt);

    public static ExecutionPackageResponse ToExecutionPackage(
        this ScenarioVersion v, Scenario s, MapMetadata? map, ExecutionPackageContent content) => new(
        ExecutionPackageFormat,
        v.Id, s.Id, s.Code, s.Name, s.Category, v.VersionNumber, v.Status, s.Status,
        SelectableForNewCampaign: v.Status == VersionStatuses.Ready && s.Status == ScenarioStatuses.Active,
        v.SchemaVersion, v.ContentHash, v.PublishedAt,
        new ExecutionPackageMap(map?.MapName ?? v.MapName, map?.CarlaVersion, map?.SpawnPointCount),
        content.ParameterValues, content.Locations,
        Runnable: content.Issues.Count == 0, content.Issues,
        JsonHelper.ToElement(content.ResolvedContent.ToJsonString())!.Value);
}
