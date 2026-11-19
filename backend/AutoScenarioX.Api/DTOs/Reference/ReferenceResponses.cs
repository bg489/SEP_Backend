namespace AutoScenarioX.Api.DTOs.Reference;

public sealed record MapResponse(string MapName, string CarlaVersion, int SpawnPointCount, string? PreviewImagePath);

/// <summary>Map kèm toàn bộ spawn point (toạ độ CARLA, mét; yaw độ) cho trình xem map ở UI.</summary>
public sealed record MapDetailResponse(
    string MapName, string CarlaVersion, int SpawnPointCount, IReadOnlyList<SpawnPointResponse> SpawnPoints);

public sealed record SpawnPointResponse(int Index, double X, double Y, double Z, double Yaw);

/// <summary>Mẫu tạo scenario: map và loại event đọc từ nội dung mẫu.</summary>
public sealed record ScenarioTemplateResponse(
    string Key, string Title, string Summary, string Category, string MapName, IReadOnlyList<string> EventTypes,
    string? Reference);

public sealed record CatalogResponse(
    string SchemaVersion,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> WeatherPresets,
    IReadOnlyList<string> EventTypes,
    IReadOnlyList<string> TriggerTypes,
    IReadOnlyList<string> ActorTypes);
