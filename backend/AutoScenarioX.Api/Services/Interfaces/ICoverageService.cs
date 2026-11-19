using AutoScenarioX.Api.DTOs.Coverage;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Thống kê scenario READY theo map, weather, category và loại event để tìm khoảng trống.</summary>
public interface ICoverageService
{
    /// <summary>scope: "latest" (mặc định) hoặc "all"; giá trị khác trả 400.</summary>
    Task<CoverageResponse> GetAsync(string? scope, CancellationToken ct = default);
}
