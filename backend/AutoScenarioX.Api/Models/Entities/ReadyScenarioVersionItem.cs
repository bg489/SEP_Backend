namespace AutoScenarioX.Api.Models.Entities;

/// <summary>
/// Dòng của view asx.v_ready_scenario_versions - ĐIỂM BÀN GIAO cho Flow 2 (chỉ đọc, không có khoá).
/// Chỉ gồm version READY của scenario ACTIVE. Không map cột content để danh sách nhẹ.
/// </summary>
public class ReadyScenarioVersionItem
{
    public Guid ScenarioVersionId { get; set; }
    public Guid ScenarioId { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public int VersionNumber { get; set; }
    public string? MapName { get; set; }
    public string? WeatherPreset { get; set; }
    public string? EventTypes { get; set; }                  // jsonb
    public int EventCount { get; set; }
    public int ActorCount { get; set; }
    public int? NpcVehicleCount { get; set; }
    public int? PedestrianCount { get; set; }
    public decimal? SpeedLimitKmh { get; set; }
    public string? EgoBlueprint { get; set; }
    public string ContentHash { get; set; } = default!;
    public DateTimeOffset PublishedAt { get; set; }
    public bool IsLatestReady { get; set; }                  // migration 103
}
