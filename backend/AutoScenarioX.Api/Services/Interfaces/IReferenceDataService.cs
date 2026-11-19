using AutoScenarioX.Api.DTOs.Reference;

namespace AutoScenarioX.Api.Services.Interfaces;

/// <summary>Dữ liệu tham chiếu cho form tạo scenario ở frontend.</summary>
public interface IReferenceDataService
{
    Task<IReadOnlyList<MapResponse>> GetMapsAsync(CancellationToken ct = default);

    /// <summary>Map đang hoạt động kèm spawn point; không có thì NotFound (MAP_NOT_FOUND).</summary>
    Task<MapDetailResponse> GetMapAsync(string mapName, CancellationToken ct = default);
    CatalogResponse GetCatalog();
    IReadOnlyList<ScenarioTemplateResponse> GetScenarioTemplates();
    string GetScenarioSchemaJson();
}
