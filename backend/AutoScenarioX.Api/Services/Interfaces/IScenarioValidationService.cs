using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IScenarioValidationService
{
    /// <summary>
    /// Chạy validate 3 tầng trên bản nháp, lưu kết quả vào asx.validation_reports
    /// và gắn report đó vào bản nháp (điều kiện để publish ở chặng C).
    /// </summary>
    Task<ValidationReportResponse> ValidateDraftAsync(Guid scenarioId, CancellationToken ct = default);
}
