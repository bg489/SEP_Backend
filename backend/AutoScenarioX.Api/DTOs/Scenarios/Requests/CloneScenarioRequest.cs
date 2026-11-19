namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

/// <summary>Body của POST /api/v1/scenarios/{id}/clone.</summary>
public sealed class CloneScenarioRequest
{
    /// <summary>Tên của scenario mới, 3-120 ký tự.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Số hiệu version muốn nhân bản. Bỏ trống = version mới nhất.</summary>
    public int? VersionNumber { get; set; }
}
