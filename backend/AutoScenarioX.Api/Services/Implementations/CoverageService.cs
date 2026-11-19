using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Coverage;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class CoverageService(IUnitOfWork uow) : ICoverageService
{
    public const string LatestScope = "latest";
    public const string AllScope = "all";

    public async Task<CoverageResponse> GetAsync(string? scope, CancellationToken ct = default)
    {
        scope = string.IsNullOrWhiteSpace(scope) ? LatestScope : scope.Trim().ToLowerInvariant();
        if (scope is not (LatestScope or AllScope))
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["scope"] = [$"Use {LatestScope} or {AllScope}."]
            });

        var rows = await uow.ScenarioVersions.ListReadyAsync(scope == LatestScope, ct);
        var maps = await uow.MapMetadata.ListActiveAsync(ct);
        var items = rows.Select(x => new CoverageItemResponse(
            x.ScenarioId, x.Code, x.Name, x.ScenarioVersionId, x.VersionNumber, x.IsLatestReady,
            x.Category, x.MapName, x.WeatherPreset, JsonHelper.ToStringArray(x.EventTypes))).ToList();

        var axes = new CoverageAxesResponse(
            Merge(maps.Select(m => m.MapName), items.Select(i => i.MapName)),
            Merge(ScenarioCatalog.WeatherPresets, items.Select(i => i.WeatherPreset)),
            Merge(ScenarioCatalog.Categories, items.Select(i => i.Category)),
            Merge(ScenarioCatalog.EventTypes, items.SelectMany(i => i.EventTypes)));

        return new CoverageResponse(scope, items.Select(i => i.ScenarioId).Distinct().Count(), items.Count, axes, items);
    }

    /// <summary>Danh mục giữ thứ tự gốc; giá trị lạ trong dữ liệu (vd map chưa nạp metadata) nối cuối theo ordinal.</summary>
    private static IReadOnlyList<string> Merge(IEnumerable<string> known, IEnumerable<string?> observed)
    {
        var result = known.Distinct().ToList();
        result.AddRange(observed.OfType<string>().Where(v => !result.Contains(v)).Distinct().Order(StringComparer.Ordinal));
        return result;
    }
}
