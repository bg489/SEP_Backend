using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

/// <summary>
/// Một sự kiện trong lịch sử thao tác của scenario (một dòng asx.audit_logs đã giải nghĩa).
/// VersionNumber: version của sự kiện VERSION, version gốc của batch biến thể, version cha của biến thể,
/// hoặc version làm nền của draft / bị clone. RelatedScenario: scenario nguồn hoặc đích của CLONE,
/// scenario cha của biến thể. Details giữ nguyên JSON đã ghi (vd tags, format, is_valid, generated_count).
/// </summary>
public sealed record ScenarioHistoryEventResponse(
    long Id,
    DateTimeOffset At,
    string Action,
    string EntityType,
    Guid EntityId,
    HistoryActorResponse Actor,
    int? VersionNumber,
    HistoryScenarioResponse? RelatedScenario,
    JsonElement Details);

public sealed record HistoryActorResponse(Guid Id, string DisplayName, string? Email);

public sealed record HistoryScenarioResponse(Guid Id, string Code, string Name);
