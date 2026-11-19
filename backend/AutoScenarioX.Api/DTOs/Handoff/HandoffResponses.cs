using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Handoff;

/// <summary>Một version READY có thể đưa vào campaign mới (dòng của v_ready_scenario_versions).</summary>
public sealed record ReadyScenarioVersionResponse(
    Guid ScenarioVersionId,
    Guid ScenarioId,
    string ScenarioCode,
    string ScenarioName,
    string Category,
    int VersionNumber,
    bool IsLatestReady,
    string ContentHash,
    string? MapName,
    string? WeatherPreset,
    string[] EventTypes,
    int EventCount,
    int ActorCount,
    int? NpcVehicleCount,
    int? PedestrianCount,
    decimal? SpeedLimitKmh,
    string? EgoBlueprint,
    DateTimeOffset PublishedAt);

/// <summary>
/// Gói chạy của một version cho runner Flow 2: nội dung đã thay $param bằng giá trị và
/// spawn_point_index đã đổi ra toạ độ theo map metadata. Định dạng: docs/handoff-flow2.md.
/// </summary>
public sealed record ExecutionPackageResponse(
    string PackageFormat,
    Guid ScenarioVersionId,
    Guid ScenarioId,
    string ScenarioCode,
    string ScenarioName,
    string Category,
    int VersionNumber,
    string VersionStatus,
    string ScenarioStatus,
    bool SelectableForNewCampaign,
    string SchemaVersion,
    string ContentHash,
    DateTimeOffset PublishedAt,
    ExecutionPackageMap Map,
    IReadOnlyDictionary<string, JsonElement> ParameterValues,
    IReadOnlyList<ResolvedLocation> Locations,
    bool Runnable,
    IReadOnlyList<string> Issues,
    JsonElement ResolvedContent);

/// <summary>Map dùng để đổi spawn point; CarlaVersion null khi chưa có metadata của map.</summary>
public sealed record ExecutionPackageMap(string? Name, string? CarlaVersion, int? SpawnPointCount);

/// <summary>Một chỗ dùng spawn_point_index trong nội dung (Path là JSON Pointer tới object chứa nó).</summary>
public sealed record ResolvedLocation(string Path, int SpawnPointIndex, SpawnTransform? Transform);

public sealed record SpawnTransform(double X, double Y, double Z, double Yaw);
