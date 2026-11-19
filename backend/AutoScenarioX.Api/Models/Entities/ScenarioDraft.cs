namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.scenario_drafts - bản nháp đang chỉnh sửa (tối đa 1 bản / scenario).</summary>
public class ScenarioDraft
{
    public Guid ScenarioId { get; set; }
    public string Content { get; set; } = default!;        // jsonb
    public Guid? BaseVersionId { get; set; }
    public Guid? LastValidationReportId { get; set; }
    public int RowVersion { get; set; } = 1;                // optimistic concurrency (DB cũng mặc định 1)
    public Guid UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Scenario Scenario { get; set; } = default!;
}
