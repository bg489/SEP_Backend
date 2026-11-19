namespace AutoScenarioX.Api.DTOs.Handoff;

/// <summary>Tham số query của GET /api/v1/scenario-versions/ready.</summary>
public sealed class ReadyVersionQueryRequest
{
    /// <summary>Tìm theo tên hoặc mã scenario (SCN-00001).</summary>
    public string? Q { get; set; }
    public string? Category { get; set; }
    public string? Map { get; set; }
    public Guid? ScenarioId { get; set; }
    /// <summary>true: chỉ lấy version READY mới nhất của mỗi scenario (is_latest_ready).</summary>
    public bool LatestOnly { get; set; }
    public int Page { get; set; } = 1;
    public int? PageSize { get; set; }
}
