namespace AutoScenarioX.Api.Repositories.Criteria;

/// <summary>Điều kiện tìm kiếm scenario do Service truyền xuống Repository.</summary>
public sealed record ScenarioSearchCriteria(
    string? Keyword,
    string? Category,
    string? Status,
    string? MapName,
    string? WeatherPreset,
    string? EventType,
    bool? HasDraft,
    IReadOnlyList<string> Tags,
    int Page,
    int PageSize);

/// <summary>Một trang dữ liệu trả về từ Repository.</summary>
public sealed record PagedData<T>(IReadOnlyList<T> Items, int TotalCount);
