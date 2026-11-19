using System.Text.Json;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Prioritization;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Services.Maps;
using AutoScenarioX.Api.Services.Prioritization;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class SimilarityService(IUnitOfWork uow, IAdministrationRepository store, ICurrentUser user) : ISimilarityService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SimilarityResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await store.SimilarityAsync(id, ct)
            ?? throw new NotFoundException("SIMILARITY_NOT_FOUND", "Comparison was not found.");
        return JsonSerializer.Deserialize<SimilarityResponse>(result.ResultJson, Json)!;
    }

    public async Task<SimilarityResponse> CompareAsync(CompareScenariosRequest request, CancellationToken ct = default)
    {
        var method = request.Method?.Trim().ToLowerInvariant();
        if (method is not ("gower" or "dtw")) throw Invalid("method", "Choose gower or dtw.");
        if (request.ScenarioVersionIds is not { Count: >= 1 and <= 100 } ids || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw Invalid("scenarioVersionIds", "Choose 1 to 100 unique nonempty version IDs.");
        if (!double.IsFinite(request.ClusterThreshold) || request.ClusterThreshold is < 0 or > 1)
            throw Invalid("clusterThreshold", "Must be between 0 and 1.");
        var loaded = await uow.ScenarioVersions.ListByIdsAsync(ids, ct);
        if (loaded.Count != ids.Count) throw new NotFoundException("VERSION_NOT_FOUND", "A requested version was not found.");
        var byId = loaded.ToDictionary(x => x.Id);
        var versions = ids.Select(id => byId[id]).ToList();
        var features = new List<SimilarityAlgorithms.MixedFeatures>();
        var routes = new List<IReadOnlyList<SimilarityAlgorithms.RoutePoint>>();
        if (method == "dtw" && versions.Select(x => x.MapName).Distinct(StringComparer.Ordinal).Count() != 1)
            throw Invalid("scenarioVersionIds", "Route DTW compares coordinates within one exact map; choose versions on the same map.");
        foreach (var version in versions)
        {
            var scenario = await uow.Scenarios.GetReadOnlyAsync(version.ScenarioId, ct)
                ?? throw new NotFoundException("SCENARIO_NOT_FOUND", "Scenario was not found.");
            features.Add(new([version.NpcVehicleCount ?? 0, version.PedestrianCount ?? 0, version.ActorCount,
                version.EventCount, (double)(version.SpeedLimitKmh ?? 0)],
                [scenario.Category, version.MapName ?? "", version.WeatherPreset ?? "", version.EgoBlueprint ?? ""]));
            if (method == "dtw") routes.Add(await RouteAsync(version, ct));
        }
        var matrix = new double[versions.Count, versions.Count];
        var pairs = new List<SimilarityPairResponse>();
        for (var a = 0; a < versions.Count; a++)
        for (var b = a + 1; b < versions.Count; b++)
        {
            var raw = method == "gower" ? SimilarityAlgorithms.Gower(features[a], features[b], features)
                : SimilarityAlgorithms.Dtw(routes[a], routes[b]);
            var distance = method == "dtw" ? raw / (1 + raw) : raw;
            matrix[a, b] = matrix[b, a] = distance;
            pairs.Add(new(versions[a].Id, versions[b].Id, Math.Round(distance, 6), Math.Round(1 - distance, 6), Math.Round(raw, 6)));
        }
        var clusters = SimilarityAlgorithms.Components(matrix, request.ClusterThreshold)
            .Select((members, index) => new SimilarityClusterResponse(index + 1, members.Select(i => versions[i].Id).ToList())).ToList();
        var response = new SimilarityResponse(Guid.NewGuid(), method, DateTimeOffset.UtcNow, clusters, pairs,
            method == "gower" ? ["npc_vehicles", "pedestrians", "actor_count", "event_count", "speed_limit_kmh", "category", "map", "weather", "ego_blueprint"]
                : ["route.start", "route.waypoints", "route.goal"],
            method == "gower"
                ? "Gower v1: equally weighted numeric range-scaled and nominal mismatch features. Clusters are threshold connected components, not HDBSCAN."
                : "Route-point DTW v1 within one exact map; rawDistance is sqrt(sum squared Cartesian costs) in meters. distance=raw/(1+raw); clusters are threshold connected components. This compares planned points, not recorded telemetry.");
        await store.SaveSimilarityAsync(new SimilarityAnalysis { Id = response.Id, Method = method, CreatedAt = response.CreatedAt,
            CreatedBy = user.UserId, ResultJson = JsonSerializer.Serialize(response, Json) }, ct);
        return response;
    }

    private async Task<IReadOnlyList<SimilarityAlgorithms.RoutePoint>> RouteAsync(ScenarioVersion version, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(version.Content);
        var root = document.RootElement;
        var route = root.GetProperty("route");
        var nodes = new List<JsonElement> { route.GetProperty("start") };
        if (route.TryGetProperty("waypoints", out var waypoints)) nodes.AddRange(waypoints.EnumerateArray());
        nodes.Add(route.GetProperty("goal"));
        if (nodes.Count > 256) throw Invalid("route", "DTW supports at most 256 explicit route points.");
        var metadata = await uow.MapMetadata.GetActiveByNameAsync(version.MapName ?? "", ct);
        var spawns = metadata is null ? [] : SpawnPointReader.Parse(metadata.SpawnPoints);
        var result = new List<SimilarityAlgorithms.RoutePoint>();
        foreach (var node in nodes)
        {
            if (node.TryGetProperty("spawn_point_index", out var index))
            {
                if (!spawns.TryGetValue(index.GetInt32(), out var spawn)) throw Invalid("route", "Map metadata is required to resolve route spawn indices.");
                result.Add(new(spawn.X, spawn.Y, spawn.Z));
            }
            else if (node.TryGetProperty("x", out var x) && node.TryGetProperty("y", out var y))
                result.Add(new(x.GetDouble(), y.GetDouble(), node.TryGetProperty("z", out var z) ? z.GetDouble() : 0));
            else throw Invalid("route", "DTW requires explicit coordinates or a resolvable spawn index; parameter references must be expanded first.");
        }
        return result;
    }

    private static RequestValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
