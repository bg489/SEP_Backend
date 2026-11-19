namespace AutoScenarioX.Api.DTOs.Tags.Requests;

public sealed class ReplaceScenarioTagsRequest
{
    /// <summary>Bộ tag mới của scenario (thay toàn bộ). Danh sách rỗng = gỡ hết. Tối đa 20.</summary>
    public List<string?>? Tags { get; set; }
}
