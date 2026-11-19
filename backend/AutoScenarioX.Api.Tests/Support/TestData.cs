using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Formats;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Validators.ScenarioContent;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Support;

/// <summary>
/// Dữ liệu test đọc thẳng từ repo (examples/, map_metadata/) để test luôn khớp bộ ví dụ
/// mà bản Python tham chiếu và tools/compare_validators.py dùng.
/// </summary>
public static class TestData
{
    public static readonly string RepoRoot = FindRepoRoot();

    public static string ExamplePath(string fileName) => Path.Combine(RepoRoot, "examples", fileName);

    public static string TelemetryPath(string fileName) => Path.Combine(RepoRoot, "examples", "telemetry", fileName);

    /// <summary>Đọc ví dụ JSON hoặc YAML thành JsonObject (YAML qua adapter thật của API).</summary>
    public static JsonObject LoadExample(string fileName)
    {
        var text = File.ReadAllText(ExamplePath(fileName));
        return fileName.EndsWith(".yaml", StringComparison.Ordinal)
            ? new YamlScenarioFormatAdapter().Parse(text)
            : new JsonScenarioFormatAdapter().Parse(text);
    }

    private static readonly Lazy<IReadOnlyDictionary<string, MapMetadata>> Maps = new(LoadMaps);

    /// <summary>12 map CARLA 0.9.16 đã nạp vào DB qua migration 104.</summary>
    public static IReadOnlyDictionary<string, MapMetadata> AllMaps => Maps.Value;

    public static MapMetadata Map(string name) => AllMaps[name];

    /// <summary>
    /// Toàn bộ luật validate trong assembly API, tạo bằng reflection: thêm luật mới (cài
    /// IScenarioValidationRule) là test tự chạy luật đó, không phải sửa danh sách ở đây.
    /// </summary>
    public static ScenarioValidator CreateValidator()
    {
        var referenceData = new ReferenceDataService(Substitute.For<IUnitOfWork>());
        var rules = typeof(IScenarioValidationRule).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IScenarioValidationRule).IsAssignableFrom(t))
            .Select(t => t.GetConstructors().Single().GetParameters().Length == 0
                ? (IScenarioValidationRule)Activator.CreateInstance(t)!
                : (IScenarioValidationRule)Activator.CreateInstance(t, referenceData)!)
            .ToList();
        return new ScenarioValidator(rules);
    }

    /// <summary>Dựng context giống ScenarioValidationService (raw cho tầng 1, bản chuẩn hoá cho tầng 2-3).</summary>
    public static ScenarioValidationContext Context(JsonObject content, bool metadataLoaded = true)
    {
        var raw = JsonSerializer.SerializeToElement(content);
        var normalized = ScenarioContentNormalizer.Normalize(raw);
        var mapName = ScenarioContentReader.AsString(normalized["map"]?["name"]);
        return new ScenarioValidationContext
        {
            Raw = raw,
            Normalized = normalized,
            MapName = mapName,
            Map = metadataLoaded && mapName is not null && AllMaps.TryGetValue(mapName, out var map) ? map : null,
            MapMetadataAvailable = metadataLoaded
        };
    }

    public static Task<ScenarioValidationResult> ValidateAsync(JsonObject content, bool metadataLoaded = true)
        => CreateValidator().ValidateAsync(Context(content, metadataLoaded));

    private static IReadOnlyDictionary<string, MapMetadata> LoadMaps()
    {
        var result = new Dictionary<string, MapMetadata>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "map_metadata"), "*.json"))
        {
            var document = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var points = document["spawn_points"]!.AsArray();
            var name = document["map_name"]!.GetValue<string>();
            result[name] = new MapMetadata
            {
                Id = Guid.NewGuid(),
                MapName = name,
                CarlaVersion = document["carla_version"]!.GetValue<string>(),
                SpawnPoints = points.ToJsonString(),
                SpawnPointCount = points.Count,   // DB tính bằng cột generated
                IsActive = true
            };
        }
        return result;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        // .sln nằm trong backend/; gốc repo là thư mục chứa cả examples/ và map_metadata/.
        while (directory is not null
               && !(Directory.Exists(Path.Combine(directory.FullName, "examples"))
                    && Directory.Exists(Path.Combine(directory.FullName, "map_metadata"))))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repo root (examples/ + map_metadata/) not found above the test output folder.");
    }
}
