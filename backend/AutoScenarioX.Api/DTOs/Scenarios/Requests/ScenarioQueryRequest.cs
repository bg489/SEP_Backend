namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

/// <summary>Tham số query của GET /api/v1/scenarios.</summary>
public sealed class ScenarioQueryRequest
{
    /// <summary>Tìm theo tên hoặc mã (SCN-00001).</summary>
    public string? Q { get; set; }
    public string? Category { get; set; }
    /// <summary>ACTIVE | ARCHIVED. Mặc định chỉ lấy ACTIVE.</summary>
    public string? Status { get; set; } = "ACTIVE";
    public string? Map { get; set; }
    /// <summary>Weather preset của version mới nhất (vd HardRainNoon).</summary>
    public string? Weather { get; set; }
    /// <summary>Version mới nhất có event loại này (vd pedestrian_crossing).</summary>
    public string? EventType { get; set; }
    public bool? HasDraft { get; set; }
    /// <summary>Lọc theo tag; lặp tham số (tag=a&amp;tag=b) = phải có đủ mọi tag.</summary>
    public string[]? Tag { get; set; }
    public int Page { get; set; } = 1;
    public int? PageSize { get; set; }
}
