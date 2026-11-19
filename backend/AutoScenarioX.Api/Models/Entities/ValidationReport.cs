namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.validation_reports - kết quả một lần validate 3 tầng.</summary>
public class ValidationReport
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public string ContentHash { get; set; } = default!;
    public bool IsValid { get; set; }
    public string? StoppedAt { get; set; }                  // schema | business | carla
    public string Errors { get; set; } = "[]";              // jsonb
    public string Warnings { get; set; } = "[]";            // jsonb
    public string SchemaVersion { get; set; } = default!;
    public string ValidatorVersion { get; set; } = default!;
    public Guid? MapMetadataId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
