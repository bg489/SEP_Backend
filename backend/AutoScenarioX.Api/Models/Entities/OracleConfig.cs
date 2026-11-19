namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.oracle_configs - bộ ngưỡng Oracle BẤT BIẾN. Đổi ngưỡng thì tạo version mới.</summary>
public class OracleConfig
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    // ---- Ngưỡng cho từng Check (xem CONTEXT.md) ----
    public decimal SpeedingToleranceKmh { get; set; } = 5;
    public decimal SpeedingMinDurationS { get; set; } = 2;
    public decimal FreezingMaxStillS { get; set; } = 10;
    public decimal TtcThresholdS { get; set; } = 2;
    public decimal RouteGoalToleranceM { get; set; } = 5;

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
