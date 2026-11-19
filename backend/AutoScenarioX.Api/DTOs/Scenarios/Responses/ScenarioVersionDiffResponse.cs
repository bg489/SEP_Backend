using AutoScenarioX.Api.Common.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

/// <summary>Khác biệt giữa hai version của cùng một scenario.</summary>
public sealed record ScenarioVersionDiffResponse(
    Guid ScenarioId,
    int FromVersion,
    int ToVersion,
    string FromContentHash,
    string ToContentHash,
    // false nghĩa là hai version chỉ khác metadata/source, không khác gì khi chạy mô phỏng
    bool ExecutionChanged,
    IReadOnlyList<JsonDiffEntry> Changes);
