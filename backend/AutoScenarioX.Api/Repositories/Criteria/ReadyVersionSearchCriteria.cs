namespace AutoScenarioX.Api.Repositories.Criteria;

/// <summary>Điều kiện tìm version READY trong view bàn giao Flow 2.</summary>
public sealed record ReadyVersionSearchCriteria(
    string? Keyword,
    string? Category,
    string? MapName,
    Guid? ScenarioId,
    bool LatestOnly,
    int Page,
    int PageSize);
