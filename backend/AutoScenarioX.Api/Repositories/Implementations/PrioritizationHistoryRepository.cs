using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public sealed class PrioritizationHistoryRepository(AsxDbContext db) : IPrioritizationHistoryRepository
{
    public async Task<IReadOnlyDictionary<Guid, VersionFailureHistory>> ReadAsync(IReadOnlyList<Guid> versionIds, CancellationToken ct = default)
    {
        var runs = await db.SimulationRuns.AsNoTracking().Where(x => versionIds.Contains(x.ScenarioVersionId))
            .Include(x => x.ScenarioVersion).ThenInclude(x => x.Scenario)
            .Include(x => x.Campaign)
            .Include(x => x.Telemetry).Include(x => x.Evaluations).ThenInclude(x => x.Violations)
            .ToListAsync(ct);
        var result = new Dictionary<Guid, VersionFailureHistory>();
        var roots = new Dictionary<Guid, Guid>();
        async Task<Guid> RootAsync(ScenarioVersion initial)
        {
            if (roots.TryGetValue(initial.Id, out var cached)) return cached;
            var version = initial;
            var seen = new HashSet<Guid>();
            while (version.SourceType == "variant" && version.ParentVersionId is { } parent && seen.Add(version.Id))
            {
                var ancestor = await db.ScenarioVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parent, ct);
                if (ancestor is null) break;
                version = ancestor;
            }
            roots[initial.Id] = version.ScenarioId;
            return version.ScenarioId;
        }
        foreach (var group in runs.GroupBy(x => x.ScenarioVersionId))
        {
            // Only the evaluation pinned by the original campaign describes its observed result.
            // ERROR/INFRA failures do not count as SUT defects or increase its failure rate.
            var judged = group.Select(run => (run, evaluation: run.Evaluations
                .OrderByDescending(x => x.EvaluatedAt).FirstOrDefault(x => x.OracleConfigId == run.Campaign.OracleConfigId)))
                .Where(x => x.evaluation?.Verdict is Verdicts.Pass or Verdicts.Fail).ToList();
            if (judged.Count == 0) continue;
            var durations = judged.Where(x => x.run.Telemetry?.DurationS > 0)
                .Select(x => (double)x.run.Telemetry!.DurationS!.Value / 60).ToList();
            var root = await RootAsync(judged[0].run.ScenarioVersion);
            var faults = judged.SelectMany(x => x.evaluation!.Violations
                    .Where(v => v.Severity == ViolationSeverities.Failing)
                    .Select(v => $"{root}:{v.CheckName}"))
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            result[group.Key] = new(group.Key, judged.Count, judged.Count(x => x.evaluation!.Verdict == Verdicts.Fail),
                durations.Count > 0 ? durations.Average() : null, faults);
        }
        return result;
    }
}
