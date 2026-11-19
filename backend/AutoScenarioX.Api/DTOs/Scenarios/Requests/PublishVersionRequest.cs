namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

/// <summary>Body của POST /api/v1/scenarios/{id}/versions (publish bản nháp thành version READY).</summary>
public sealed class PublishVersionRequest
{
    /// <summary>Ghi chú thay đổi, tối đa 1000 ký tự.</summary>
    public string? ChangeNote { get; set; }
}
