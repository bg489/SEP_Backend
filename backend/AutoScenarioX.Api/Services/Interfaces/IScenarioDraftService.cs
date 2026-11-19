using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Nghiệp vụ trên bản nháp: xem, tạo, lưu, huỷ. (Validate nằm ở IScenarioValidationService.)</summary>
public interface IScenarioDraftService
{
    Task<ScenarioDraftResponse> GetAsync(Guid scenarioId, CancellationToken ct = default);

    /// <summary>
    /// Tạo bản nháp mới (tối đa 1 / scenario). Nội dung lấy từ version fromVersionNumber nếu có,
    /// không thì từ version mới nhất; chưa có version nào thì dùng mẫu.
    /// </summary>
    Task<ScenarioDraftResponse> CreateAsync(Guid scenarioId, int? fromVersionNumber = null, CancellationToken ct = default);

    /// <summary>Lưu nội dung bản nháp. ifMatchRowVersion lấy từ header If-Match; lệch thì 409.</summary>
    Task<ScenarioDraftResponse> SaveAsync(Guid scenarioId, SaveDraftRequest request, int ifMatchRowVersion, CancellationToken ct = default);

    /// <summary>Huỷ bản nháp (bỏ các thay đổi chưa publish).</summary>
    Task DeleteAsync(Guid scenarioId, CancellationToken ct = default);
}
