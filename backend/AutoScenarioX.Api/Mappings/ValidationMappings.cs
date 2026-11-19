using System.Text.Json;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Mappings;

public static class ValidationMappings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    /// <summary>Dạng lưu trong cột jsonb: [{"tier":"business","path":"/route/goal","message":"..."}]</summary>
    public static string ToJsonArray(this IReadOnlyList<ValidationIssue> issues)
        => JsonSerializer.Serialize(
            issues.Select(i => new
            {
                tier = i.Tier.ToDbValue(),
                path = i.Path,
                message = i.Message
            }),
            SerializerOptions);

    public static ValidationReportResponse ToResponse(
        this ValidationReport report,
        IReadOnlyList<ValidationIssue> errors,
        IReadOnlyList<ValidationIssue> warnings,
        int? duplicateOfVersionNumber = null) => new(
            report.Id,
            report.ScenarioId,
            report.ContentHash,
            report.IsValid,
            report.StoppedAt,
            errors.Select(ToResponse).ToList(),
            warnings.Select(ToResponse).ToList(),
            report.SchemaVersion,
            report.ValidatorVersion,
            report.CreatedAt,
            duplicateOfVersionNumber);

    private static ValidationIssueResponse ToResponse(ValidationIssue issue)
        => new(issue.Tier.ToDbValue(), issue.Path, issue.Message);
}
