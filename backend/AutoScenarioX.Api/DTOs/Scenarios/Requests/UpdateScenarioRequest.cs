namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

/// <summary>
/// Body của PATCH /api/v1/scenarios/{id}. Chỉ sửa hồ sơ, KHÔNG đụng tới nội dung scenario
/// (nội dung nằm trong bản nháp / version). Trường để null = không đổi.
/// </summary>
public sealed class UpdateScenarioRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
}
