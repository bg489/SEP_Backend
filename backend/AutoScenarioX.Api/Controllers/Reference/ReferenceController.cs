using AutoScenarioX.Api.DTOs.Reference;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Reference;

/// <summary>Dữ liệu tham chiếu cho frontend: map, danh mục giá trị, JSON Schema.</summary>
[ApiController]
[Route("api/v1/reference")]
public class ReferenceController(IReferenceDataService referenceService) : ControllerBase
{
    [HttpGet("maps")]
    [ProducesResponseType<IReadOnlyList<MapResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MapResponse>>> Maps(CancellationToken ct)
        => Ok(await referenceService.GetMapsAsync(ct));

    /// <summary>Spawn point của một map (toạ độ CARLA) cho trình xem map. 404 MAP_NOT_FOUND nếu chưa nạp metadata.</summary>
    [HttpGet("maps/{mapName}")]
    [ProducesResponseType<MapDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MapDetailResponse>> Map(string mapName, CancellationToken ct)
        => Ok(await referenceService.GetMapAsync(mapName, ct));

    [HttpGet("catalog")]
    [ProducesResponseType<CatalogResponse>(StatusCodes.Status200OK)]
    public ActionResult<CatalogResponse> Catalog() => Ok(referenceService.GetCatalog());

    /// <summary>Mẫu kịch bản dựng sẵn để tạo scenario (POST /scenarios với "template").</summary>
    [HttpGet("scenario-templates")]
    [ProducesResponseType<IReadOnlyList<ScenarioTemplateResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ScenarioTemplateResponse>> ScenarioTemplates()
        => Ok(referenceService.GetScenarioTemplates());

    /// <summary>JSON Schema v1.0 của scenario (frontend có thể dùng để validate form).</summary>
    [HttpGet("scenario-schema")]
    [Produces("application/schema+json")]
    public IActionResult ScenarioSchema()
        => Content(referenceService.GetScenarioSchemaJson(), "application/schema+json");
}
