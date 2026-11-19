namespace AutoScenarioX.Api.DTOs.Tags.Responses;

/// <summary>Tag đang dùng; ActiveScenarioCount không tính scenario đã lưu trữ.</summary>
public sealed record TagResponse(string Name, int ScenarioCount, int ActiveScenarioCount);

public sealed record ScenarioTagsResponse(Guid ScenarioId, IReadOnlyList<string> Tags);
