namespace AutoScenarioX.Api.Models.Entities;

/// <summary>Dòng của view asx.v_scenario_repository (chỉ đọc, không có khoá).</summary>
public class ScenarioRepositoryItem
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int LatestVersionNumber { get; set; }
    public Guid? LatestVersionId { get; set; }
    public string? LatestVersionStatus { get; set; }
    public string? MapName { get; set; }
    public string? WeatherPreset { get; set; }
    public string? EventTypes { get; set; }                  // jsonb
    public int? EventCount { get; set; }
    public bool HasDraft { get; set; }
    public string[] Tags { get; set; } = [];
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
