namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

/// <summary>Body của POST /api/v1/scenarios. Tạo hồ sơ scenario kèm một bản nháp mẫu.</summary>
public sealed class CreateScenarioRequest
{
    /// <summary>Tên scenario, 3-120 ký tự.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mô tả, tối đa 2000 ký tự.</summary>
    public string? Description { get; set; }

    /// <summary>Một trong ScenarioCatalog.Categories (pedestrian, cut_in, braking...).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Key mẫu trong GET /reference/scenario-templates; bỏ trống = bản nháp trống tối thiểu.</summary>
    public string? Template { get; set; }
}
