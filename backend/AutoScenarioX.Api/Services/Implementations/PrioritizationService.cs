using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Prioritization;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Services.Prioritization;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class PrioritizationService(IUnitOfWork uow, IPrioritizationHistoryRepository history) : IPrioritizationService
{
    private const int MaxVersions = 100;
    private const int DefaultLimit = 40;

    public IReadOnlyList<PrioritizationStrategyInfo> ListStrategies() =>
    [
        new(PrioritizationStrategies.ProposedFailureHistory, "Proposed · failure history",
            "Balances observed Oracle failure rate with diversity. Versions without judged history use an explicitly labeled content-risk fallback."),
        new(PrioritizationStrategies.SeverityFirst, "Severity-first",
            "Orders by estimated severity / risk derived from category, weather and events."),
        new(PrioritizationStrategies.RiskFirst, "Risk-first",
            "Orders strictly by heuristic risk score."),
        new(PrioritizationStrategies.Diversity, "Diversity",
            "Greedy selection maximizing feature-space distance between scenarios."),
        new(PrioritizationStrategies.RandomBaseline, "Random baseline",
            "Seeded shuffle for baseline comparison (APFD experiments).")
    ];

    public async Task<PrioritizationRunResponse> RankAsync(PrioritizeRequest request, CancellationToken ct = default)
    {
        Validate(request);

        var strategy = request.Strategy.Trim().ToLowerInvariant();
        var limit = Math.Clamp(request.Params?.Limit ?? DefaultLimit, 1, MaxVersions);

        var versions = await LoadVersionsAsync(request.ScenarioVersionIds, limit, ct);
        if (versions.Count == 0)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["scenarioVersionIds"] = ["No READY scenario versions available to prioritize."]
            });

        var scenarioIds = versions.Select(v => v.ScenarioId).Distinct().ToArray();
        var scenarios = new Dictionary<Guid, Scenario>();
        foreach (var id in scenarioIds)
        {
            var scenario = await uow.Scenarios.GetReadOnlyAsync(id, ct)
                ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {id} was not found.");
            scenarios[id] = scenario;
        }

        // Chỉ xếp version READY (hoặc cho phép DEPRECATED nếu client chỉ định tường minh).
        var ready = versions
            .Where(v => v.Status == VersionStatuses.Ready ||
                        (request.ScenarioVersionIds is { Count: > 0 }))
            .Where(v => scenarios.ContainsKey(v.ScenarioId))
            .GroupBy(v => v.Id)
            .Select(g => g.First())
            .Take(MaxVersions)
            .ToList();

        if (ready.Count == 0)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["scenarioVersionIds"] = ["None of the requested versions could be prioritized."]
            });

        var extracted = ready.Select(v =>
        {
            var features = ScenarioFeatureExtractor.Extract(v, scenarios[v.ScenarioId]);
            return (Version: v, Features: features);
        }).ToList();

        var observed = await history.ReadAsync(ready.Select(x => x.Id).ToList(), ct);
        var normalized = ScenarioFeatureExtractor.MinMaxNormalize(extracted.Select(x => x.Features.Vector).ToList());
        var candidates = extracted.Select((x, i) => new PrioritizationEngine.CandidateItem(
            i, x.Version.Id, x.Features, normalized[i], observed.GetValueOrDefault(x.Version.Id))).ToList();

        var ranked = PrioritizationEngine.Rank(candidates, strategy, request.Params);
        var ordered = new List<RankedScenarioItem>(ranked.Order.Count);
        for (var rank = 0; rank < ranked.Order.Count; rank++)
        {
            var idx = ranked.Order[rank];
            var version = ready[idx];
            var scenario = scenarios[version.ScenarioId];
            var features = extracted[idx].Features;
            ordered.Add(new RankedScenarioItem(
                Rank: rank + 1,
                ScenarioVersionId: version.Id,
                ScenarioId: scenario.Id,
                ScenarioCode: scenario.Code,
                ScenarioName: scenario.Name,
                Category: scenario.Category,
                MapName: version.MapName,
                WeatherPreset: version.WeatherPreset,
                Severity: features.Severity,
                FailureSignalEstimate: features.FailureSignalEstimate,
                EstimatedCostMinutes: features.EstimatedCostMinutes,
                Score: ranked.Scores[idx],
                Rationale: ranked.Rationales[idx],
                ObservedRuns: candidates[idx].History?.EvaluatedRuns ?? 0,
                ObservedFailures: candidates[idx].History?.FailedRuns ?? 0));
        }

        IReadOnlyList<IReadOnlyList<double>>? matrixPayload = null;
        if (ready.Count <= 40)
        {
            var n = ready.Count;
            var rows = new List<IReadOnlyList<double>>(n);
            for (var a = 0; a < n; a++)
            {
                var row = new double[n];
                for (var b = 0; b < n; b++)
                    row[b] = Math.Round(ranked.Distance[a, b], 4);
                rows.Add(row);
            }
            matrixPayload = rows;
        }

        return new PrioritizationRunResponse(
            RunId: Guid.NewGuid(),
            Strategy: strategy,
            CreatedAt: DateTimeOffset.UtcNow,
            Metrics: ranked.Metrics,
            Ordered: ordered,
            FeatureNames: ScenarioFeatureExtractor.FeatureNames,
            DistanceMatrix: matrixPayload);
    }

    private async Task<List<ScenarioVersion>> LoadVersionsAsync(
        IReadOnlyList<Guid>? ids,
        int limit,
        CancellationToken ct)
    {
        if (ids is { Count: > 0 })
        {
            var loaded = await uow.ScenarioVersions.ListByIdsAsync(ids, ct);
            var missing = ids.Except(loaded.Select(v => v.Id)).ToArray();
            if (missing.Length > 0)
                throw new NotFoundException(
                    ErrorCodes.VersionNotFound,
                    $"Scenario version(s) not found: {string.Join(", ", missing.Take(5))}{(missing.Length > 5 ? "…" : "")}");

            // Giữ thứ tự request khi có thể
            var map = loaded.ToDictionary(v => v.Id);
            return ids.Where(map.ContainsKey).Select(id => map[id]).ToList();
        }

        var page = await uow.ScenarioVersions.SearchReadyAsync(
            new ReadyVersionSearchCriteria(
                Keyword: null,
                Category: null,
                MapName: null,
                ScenarioId: null,
                LatestOnly: true,
                Page: 1,
                PageSize: limit),
            ct);

        var versionIds = page.Items.Select(x => x.ScenarioVersionId).ToArray();
        var versions = await uow.ScenarioVersions.ListByIdsAsync(versionIds, ct);
        var byId = versions.ToDictionary(v => v.Id);
        return versionIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private static void Validate(PrioritizeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var strategy = (request.Strategy ?? "").Trim().ToLowerInvariant();
        if (!PrioritizationStrategies.All.Contains(strategy))
            errors["strategy"] = [$"Invalid strategy. Allowed: {string.Join(", ", PrioritizationStrategies.All)}"];

        if (request.ScenarioVersionIds is { Count: > MaxVersions })
            errors["scenarioVersionIds"] = [$"At most {MaxVersions} versions can be prioritized in one request."];
        if (request.ScenarioVersionIds is { } ids && (ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count))
            errors["scenarioVersionIds"] = ["Version IDs must be non-empty and unique."];

        if (request.Params?.DiversityWeight is < 0 or > 1)
            errors["params.diversityWeight"] = ["Must be between 0 and 1."];
        if (request.Params?.RiskWeight is < 0 or > 1)
            errors["params.riskWeight"] = ["Must be between 0 and 1."];
        if (request.Params?.DiversityWeight is { } dw && !double.IsFinite(dw)) errors["params.diversityWeight"] = ["Must be finite."];
        if (request.Params?.RiskWeight is { } rw && !double.IsFinite(rw)) errors["params.riskWeight"] = ["Must be finite."];
        if (request.Params?.Limit is < 1 or > MaxVersions) errors["params.limit"] = ["Must be between 1 and 100."];

        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
