namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.map_metadata - spawn point của từng map CARLA (validation tầng 3, dropdown UI).</summary>
public class MapMetadata
{
    public Guid Id { get; set; }
    public string MapName { get; set; } = default!;
    public string CarlaVersion { get; set; } = default!;
    public string SpawnPoints { get; set; } = "[]";         // jsonb
    public int SpawnPointCount { get; set; }                 // generated
    public string? PreviewImagePath { get; set; }
    public bool IsActive { get; set; }
    public Guid? ImportedBy { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
