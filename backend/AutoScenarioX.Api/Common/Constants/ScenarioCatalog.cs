namespace AutoScenarioX.Api.Common.Constants;

/// <summary>
/// Danh mục giá trị cho phép - PHẢI khớp với Resources/scenario.schema.json và CHECK trong DB.
/// Dùng cho dropdown ở frontend (GET /api/v1/reference/catalog).
/// </summary>
public static class ScenarioCatalog
{
    public static readonly IReadOnlyList<string> Categories =
    [
        "pedestrian", "cut_in", "braking", "traffic_light", "following",
        "intersection", "obstacle", "free_driving", "other"
    ];

    public static readonly IReadOnlyList<string> WeatherPresets =
    [
        "ClearNoon", "CloudyNoon", "WetNoon", "WetCloudyNoon", "SoftRainNoon", "MidRainyNoon", "HardRainNoon",
        "ClearSunset", "CloudySunset", "WetSunset", "WetCloudySunset", "SoftRainSunset", "MidRainSunset", "HardRainSunset",
        "ClearNight", "CloudyNight", "WetNight", "WetCloudyNight", "SoftRainNight", "MidRainyNight", "HardRainNight"
    ];

    public static readonly IReadOnlyList<string> EventTypes =
    [
        "pedestrian_crossing", "vehicle_cut_in", "lead_vehicle_brake", "traffic_light_override", "vehicle_crossing"
    ];

    public static readonly IReadOnlyList<string> TriggerTypes =
    [
        "ego_distance_to_actor", "ego_distance_to_location", "simulation_time", "after_event", "time_to_collision"
    ];

    public static readonly IReadOnlyList<string> ActorTypes =
    [
        "vehicle", "pedestrian", "bicycle", "static_obstacle"
    ];

    public const string CurrentSchemaVersion = "1.0";
}
