namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.violations - một Check bị vi phạm tại một thời điểm trong Evaluation.</summary>
public class ViolationEntity
{
    public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public string CheckName { get; set; } = default!;      // "collision", "speeding", v.v.
    public string Severity { get; set; } = default!;        // "FAILING" | "WARNING"
    public decimal T { get; set; }                          // thời điểm mô phỏng (giây)
    public decimal? DurationS { get; set; }                 // cho speeding/freezing
    public string Details { get; set; } = "{}";             // jsonb - chi tiết tuỳ Check
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation property
    public Evaluation Evaluation { get; set; } = default!;
}

/// <summary>Tên các Check của Oracle (khớp CHECK trong DB).</summary>
public static class CheckNames
{
    public const string Collision = "collision";
    public const string Speeding = "speeding";
    public const string Freezing = "freezing";
    public const string Ttc = "ttc";
    public const string LaneInvasion = "lane_invasion";
    public const string RedLight = "red_light";
    public const string RouteIncomplete = "route_incomplete";
}

/// <summary>Mức nghiêm trọng của vi phạm (khớp CHECK trong DB).</summary>
public static class ViolationSeverities
{
    public const string Failing = "FAILING";
    public const string Warning = "WARNING";
}
