namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.scenario_versions - version BẤT BIẾN. Điểm bàn giao cho Flow 2.</summary>
public class ScenarioVersion
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public int VersionNumber { get; set; }
    public string SchemaVersion { get; set; } = "1.0";
    public string Content { get; set; } = default!;         // jsonb - toàn bộ cấu hình scenario
    public string ContentHash { get; set; } = default!;

    // ---- Cột generated: DB tự tách từ Content (chỉ đọc) ----
    public string? MapName { get; set; }
    public string? WeatherPreset { get; set; }
    public string? EventTypes { get; set; }                  // jsonb mảng chuỗi
    public int ActorCount { get; set; }
    public int EventCount { get; set; }
    public int? NpcVehicleCount { get; set; }
    public int? PedestrianCount { get; set; }
    public decimal? SpeedLimitKmh { get; set; }
    public string? EgoBlueprint { get; set; }

    public string Status { get; set; } = VersionStatuses.Ready;
    public string? ChangeNote { get; set; }
    public string SourceType { get; set; } = default!;
    public Guid? ParentVersionId { get; set; }
    public Guid? ImportRecordId { get; set; }
    public Guid ValidationReportId { get; set; }
    public Guid PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Guid? DeprecatedBy { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }

    public Scenario Scenario { get; set; } = default!;
}

public static class VersionStatuses
{
    public const string Ready = "READY";
    public const string Deprecated = "DEPRECATED";
}
