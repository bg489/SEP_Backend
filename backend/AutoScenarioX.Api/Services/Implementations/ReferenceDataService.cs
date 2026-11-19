using System.Reflection;
using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Reference;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Services.Maps;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ReferenceDataService(IUnitOfWork uow) : IReferenceDataService
{
    // Tên LogicalName khai báo trong AutoScenarioX.Api.csproj
    private const string SchemaResourceName = "AutoScenarioX.Api.Resources.scenario.schema.json";
    private static readonly Lazy<string> SchemaJson = new(LoadSchema);

    public async Task<IReadOnlyList<MapResponse>> GetMapsAsync(CancellationToken ct = default)
    {
        var maps = await uow.MapMetadata.ListActiveAsync(ct);
        return maps.Select(m => m.ToResponse()).ToList();
    }

    public async Task<MapDetailResponse> GetMapAsync(string mapName, CancellationToken ct = default)
    {
        var map = await uow.MapMetadata.GetActiveByNameAsync(mapName, ct)
            ?? throw new NotFoundException(ErrorCodes.MapNotFound, $"Map metadata for '{mapName}' has not been loaded.");
        var points = SpawnPointReader.Parse(map.SpawnPoints)
            .OrderBy(p => p.Key)
            .Select(p => new SpawnPointResponse(p.Key, p.Value.X, p.Value.Y, p.Value.Z, p.Value.Yaw))
            .ToList();
        return new MapDetailResponse(map.MapName, map.CarlaVersion, map.SpawnPointCount, points);
    }

    public CatalogResponse GetCatalog() => new(
        ScenarioCatalog.CurrentSchemaVersion,
        ScenarioCatalog.Categories,
        ScenarioCatalog.WeatherPresets,
        ScenarioCatalog.EventTypes,
        ScenarioCatalog.TriggerTypes,
        ScenarioCatalog.ActorTypes);

    public IReadOnlyList<ScenarioTemplateResponse> GetScenarioTemplates() => TemplateList.Value;

    private static readonly Lazy<IReadOnlyList<ScenarioTemplateResponse>> TemplateList = new(() =>
        ScenarioContentTemplate.Templates.Select(t =>
        {
            using var doc = JsonDocument.Parse(ScenarioContentTemplate.LoadTemplate(t.Key)!);
            var root = doc.RootElement;
            var events = root.TryGetProperty("events", out var list)
                ? list.EnumerateArray().Select(e => e.GetProperty("type").GetString()!).Distinct().ToList()
                : [];
            return new ScenarioTemplateResponse(t.Key, t.Title, t.Summary, t.Category,
                root.GetProperty("map").GetProperty("name").GetString()!, events, t.Reference);
        }).ToList());

    public string GetScenarioSchemaJson() => SchemaJson.Value;

    private static string LoadSchema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy embedded resource '{SchemaResourceName}'. Kiểm tra EmbeddedResource trong .csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
