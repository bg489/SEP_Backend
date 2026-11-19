using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Responses;

public sealed record ScenarioDraftResponse(
    Guid ScenarioId,
    Guid? BaseVersionId,
    Guid? LastValidationReportId,
    int RowVersion,
    DateTimeOffset UpdatedAt,
    JsonElement? Content);
