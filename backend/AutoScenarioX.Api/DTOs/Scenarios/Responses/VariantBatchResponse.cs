using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

/// <summary>CartesianCount = số tổ hợp nếu sinh đủ grid, để so với GeneratedCount khi dùng pairwise.</summary>
public sealed record VariantBatchSummaryResponse(
    Guid Id, Guid BaseVersionId, JsonElement ParameterGrid, string Strategy, long CartesianCount,
    int RequestedCount, int GeneratedCount, string? Note, Guid CreatedBy, DateTimeOffset CreatedAt);

public sealed record VariantScenarioResponse(
    Guid Id, string Code, string Name, string Status,
    Guid? LatestVersionId, int LatestVersionNumber, bool HasDraft);

public sealed record VariantBatchResponse(
    VariantBatchSummaryResponse Batch, IReadOnlyList<VariantScenarioResponse> Scenarios);

/// <summary>Kết quả kiểm tra tạm; batch bị từ chối nên không có ID report/scenario đã lưu.</summary>
public sealed record VariantValidationFailureResponse(
    string? StoppedAt, IReadOnlyList<ValidationIssueResponse> Errors, IReadOnlyList<ValidationIssueResponse> Warnings);
