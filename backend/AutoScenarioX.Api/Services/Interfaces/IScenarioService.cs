using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Nghiệp vụ trên hồ sơ scenario (Repository, chi tiết, tạo, sửa hồ sơ).</summary>
public interface IScenarioService
{
    Task<PagedResult<ScenarioListItemResponse>> SearchAsync(ScenarioQueryRequest request, CancellationToken ct = default);
    Task<ScenarioDetailResponse> GetDetailAsync(Guid scenarioId, CancellationToken ct = default);

    /// <summary>Tạo scenario mới kèm bản nháp mẫu. Trả về hồ sơ vừa tạo.</summary>
    Task<ScenarioDetailResponse> CreateAsync(CreateScenarioRequest request, CancellationToken ct = default);

    /// <summary>Sửa tên / mô tả / category. Không sinh version mới; ghi audit UPDATE_PROFILE với các trường đã đổi.</summary>
    Task<ScenarioDetailResponse> UpdateProfileAsync(Guid scenarioId, UpdateScenarioRequest request, CancellationToken ct = default);

    /// <summary>Đưa scenario vào lưu trữ: ẩn khỏi danh sách mặc định, không sửa/publish được nữa.</summary>
    Task<ScenarioDetailResponse> ArchiveAsync(Guid scenarioId, CancellationToken ct = default);

    /// <summary>Khôi phục scenario đã lưu trữ về trạng thái ACTIVE.</summary>
    Task<ScenarioDetailResponse> RestoreAsync(Guid scenarioId, CancellationToken ct = default);

    /// <summary>
    /// Nhân bản một version thành scenario mới (dùng cho biến thể tham số, thử nghiệm).
    /// Scenario mới có origin_type = clone và một bản nháp chứa nội dung đã sao.
    /// </summary>
    Task<ScenarioDetailResponse> CloneAsync(Guid scenarioId, CloneScenarioRequest request, CancellationToken ct = default);
}
