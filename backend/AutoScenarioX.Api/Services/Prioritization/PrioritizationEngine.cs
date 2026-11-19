using AutoScenarioX.Api.DTOs.Prioritization;
using AutoScenarioX.Api.Repositories.Interfaces;

namespace AutoScenarioX.Api.Services.Prioritization;

/// <summary>
/// Chuẩn hóa → distance matrix → sinh / đánh giá thứ tự theo strategy.
/// Chưa có lịch sử fail thật: APFD/APFDc là ước lượng heuristic từ ranking + risk.
/// </summary>
public static class PrioritizationEngine
{
    public sealed record CandidateItem(
        int Index,
        Guid VersionId,
        ScenarioFeatureExtractor.ExtractedFeatures Features,
        double[] Normalized,
        VersionFailureHistory? History = null);

    public sealed record RankResult(
        IReadOnlyList<int> Order,
        IReadOnlyList<double> Scores,
        IReadOnlyList<string> Rationales,
        PrioritizationMetrics Metrics,
        double[,] Distance);

    public static RankResult Rank(
        IReadOnlyList<CandidateItem> items,
        string strategy,
        PrioritizationParams? parameters)
    {
        var diversityW = Clamp01(parameters?.DiversityWeight ?? 0.45);
        var riskW = Clamp01(parameters?.RiskWeight ?? 0.55);
        var sum = diversityW + riskW;
        if (sum < 1e-9) { diversityW = 0.45; riskW = 0.55; sum = 1; }
        diversityW /= sum;
        riskW /= sum;
        var seed = parameters?.Seed ?? 42;

        var vectors = items.Select(x => x.Normalized).ToList();
        var distance = ScenarioFeatureExtractor.DistanceMatrix(vectors);
        if (items.Count == 0) throw new ArgumentException("At least one candidate is required.", nameof(items));
        var riskScores = items.Select(x => strategy == PrioritizationStrategies.ProposedFailureHistory && x.History is { EvaluatedRuns: > 0 } h
            ? h.FailedRuns / (double)h.EvaluatedRuns : RiskScore(x)).ToArray();

        var order = strategy switch
        {
            PrioritizationStrategies.SeverityFirst => OrderBySeverity(items, riskScores),
            PrioritizationStrategies.RiskFirst => OrderByRisk(riskScores),
            PrioritizationStrategies.Diversity => GreedyDiversity(distance, riskScores, diversityW: 1, riskW: 0),
            PrioritizationStrategies.RandomBaseline => RandomOrder(items.Count, seed),
            _ => GreedyDiversity(distance, riskScores, diversityW, riskW) // proposed_failure_history
        };

        var scores = new double[items.Count];
        var rationales = new string[items.Count];
        for (var rank = 0; rank < order.Count; rank++)
        {
            var idx = order[rank];
            var diversityBonus = AverageDistanceToEarlier(distance, order, rank, idx);
            var combined = strategy == PrioritizationStrategies.RandomBaseline
                ? 100.0 - rank
                : Math.Clamp(riskScores[idx] * riskW * 100 + diversityBonus * diversityW * 40 + (items.Count - rank), 0, 100);
            scores[idx] = Math.Round(combined, 1);
            rationales[idx] = BuildRationale(strategy, items[idx], riskScores[idx], diversityBonus, rank + 1);
        }

        var metrics = EstimateMetrics(order, riskScores, items);
        var observedRuns = items.Sum(x => x.History?.EvaluatedRuns ?? 0);
        var status = observedRuns == 0 ? "no_data" : items.All(x => x.History is { EvaluatedRuns: > 0 }) ? "complete" : "partial";
        var observed = ObservedMetrics(order, items);
        metrics = metrics with { HistoryStatus = status, ObservedRuns = observedRuns, ObservedApfd = observed.Apfd, ObservedApfdc = observed.Apfdc,
            Note = observedRuns == 0 ? "No judged Oracle history. Ordering falls back to content risk; APFD estimate is a proxy, not a measured result."
                : $"Oracle history covers {items.Count(x => x.History is { EvaluatedRuns: > 0 })}/{items.Count} versions and {observedRuns} runs. Observed APFD uses distinct scenario/check faults; uncovered versions use content-risk fallback. Heuristic estimates remain labeled separately." };
        return new RankResult(order, scores, rationales, metrics, distance);
    }

    private static double RiskScore(CandidateItem item)
    {
        return item.Features.Severity switch
        {
            "Critical" => 0.95,
            "High" => 0.75,
            "Medium" => 0.5,
            _ => 0.3
        };
    }

    private static List<int> OrderBySeverity(IReadOnlyList<CandidateItem> items, double[] riskScores)
        => Enumerable.Range(0, items.Count)
            .OrderByDescending(i => riskScores[i])
            .ThenByDescending(i => items[i].Features.FailureSignalEstimate)
            .ThenBy(i => items[i].VersionId)
            .ToList();

    private static List<int> OrderByRisk(double[] riskScores)
        => Enumerable.Range(0, riskScores.Length)
            .OrderByDescending(i => riskScores[i])
            .ThenBy(i => i)
            .ToList();

    private static List<int> RandomOrder(int n, int seed)
    {
        var order = Enumerable.Range(0, n).ToList();
        var rng = new Random(seed);
        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    /// <summary>Greedy: mỗi bước chọn phần tử còn lại tối đa (risk + khoảng cách trung bình tới đã chọn).</summary>
    private static List<int> GreedyDiversity(double[,] distance, double[] riskScores, double diversityW, double riskW)
    {
        var n = riskScores.Length;
        var remaining = Enumerable.Range(0, n).ToHashSet();
        var order = new List<int>(n);

        // Bắt đầu bằng risk cao nhất
        var first = remaining.OrderByDescending(i => riskScores[i]).ThenBy(i => i).First();
        order.Add(first);
        remaining.Remove(first);

        while (remaining.Count > 0)
        {
            var best = remaining
                .Select(i =>
                {
                    var avgDist = order.Average(j => distance[i, j]);
                    var score = riskScores[i] * riskW + avgDist * diversityW;
                    return (i, score);
                })
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.i)
                .First().i;
            order.Add(best);
            remaining.Remove(best);
        }
        return order;
    }

    private static double AverageDistanceToEarlier(double[,] distance, IReadOnlyList<int> order, int rank, int idx)
    {
        if (rank == 0) return 0;
        double sum = 0;
        for (var r = 0; r < rank; r++) sum += distance[idx, order[r]];
        return sum / rank;
    }

    private static string BuildRationale(
        string strategy,
        CandidateItem item,
        double risk,
        double diversityBonus,
        int rank)
    {
        var severity = item.Features.Severity;
        return strategy switch
        {
            PrioritizationStrategies.RandomBaseline =>
                $"Random baseline (seeded) · rank {rank}",
            PrioritizationStrategies.SeverityFirst =>
                $"{severity} severity · {item.Features.RationaleHints}",
            PrioritizationStrategies.RiskFirst =>
                $"Risk-first · score {risk:0.00} · {item.Features.RationaleHints}",
            PrioritizationStrategies.Diversity =>
                $"Diversity greedy · avg distance {diversityBonus:0.00} · {item.Features.RationaleHints}",
            _ =>
                item.History is { EvaluatedRuns: > 0 } h
                    ? $"Observed Oracle history: {h.FailedRuns}/{h.EvaluatedRuns} FAIL · failure rate {risk:0.00} · diversity {diversityBonus:0.00}"
                    : $"No judged Oracle history: content-risk fallback · {severity} · diversity {diversityBonus:0.00} · {item.Features.RationaleHints}"
        };
    }

    private static PrioritizationMetrics EstimateMetrics(
        IReadOnlyList<int> order,
        double[] riskScores,
        IReadOnlyList<CandidateItem> items)
    {
        // Proxy "fault" = Critical only (risk >= 0.9). High+ Critical làm m=n → APFD luôn 0.5 mọi thứ tự.
        // Khi không có Critical: lấy top ~40% risk trong suite để metric vẫn phân biệt được strategy.
        var critical = order
            .Select((idx, rank) => (idx, rank))
            .Where(x => riskScores[x.idx] >= 0.9)
            .Select(x => x.rank)
            .ToList();
        List<int> faultIndices;
        if (critical.Count > 0)
        {
            faultIndices = critical;
        }
        else
        {
            var rankedByRisk = order
                .Select((idx, rank) => (idx, rank, risk: riskScores[idx]))
                .OrderByDescending(x => x.risk)
                .ToList();
            var take = Math.Max(1, (int)Math.Ceiling(order.Count * 0.4));
            var top = rankedByRisk.Take(take).Select(x => x.idx).ToHashSet();
            faultIndices = order
                .Select((idx, rank) => (idx, rank))
                .Where(x => top.Contains(x.idx))
                .Select(x => x.rank)
                .ToList();
        }

        var m = faultIndices.Count;
        var n = order.Count;
        double apfd = 0.5;
        if (m > 0 && n > 0)
        {
            // APFD = 1 - (sum TF_i)/(n*m) + 1/(2n)
            var sumTf = faultIndices.Sum(r => r + 1);
            apfd = 1.0 - sumTf / (n * (double)m) + 1.0 / (2 * n);
            apfd = Math.Clamp(apfd, 0, 1);
        }

        var totalCost = items.Sum(x => x.Features.EstimatedCostMinutes);
        var earlyWindow = Math.Max(1, (int)Math.Ceiling(n * 0.4));
        var earlyFaults = faultIndices.Count(r => r < earlyWindow);
        var apfdc = Math.Clamp(apfd * 0.9 + (m > 0 ? earlyFaults / (double)m * 0.1 : 0), 0, 1);

        return new PrioritizationMetrics(
            ApfdEstimate: Math.Round(apfd, 3),
            ApfdcEstimate: Math.Round(apfdc, 3),
            FaultsFoundEarlyEstimate: earlyFaults,
            FaultsTotalEstimate: Math.Max(m, 1),
            EstimatedSuiteMinutes: Math.Round(totalCost, 1),
            Note: "Heuristic APFD from Critical/content-risk proxy (no oracle failure history yet). Suite time is order-independent.");
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    public static (double? Apfd, double? Apfdc) ObservedMetrics(IReadOnlyList<int> order, IReadOnlyList<CandidateItem> items)
    {
        // A fault is discovered once at its first ranked candidate, even after repeated runs.
        var first = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var rank = 0; rank < order.Count; rank++)
            foreach (var fault in items[order[rank]].History?.FaultIds ?? []) first.TryAdd(fault, rank);
        if (first.Count == 0 || order.Count == 0) return (null, null);
        var apfd = 1 - first.Values.Sum(x => x + 1) / (double)(order.Count * first.Count) + 1d / (2 * order.Count);
        var costs = order.Select(x => items[x].History?.MeanDurationMinutes).ToArray();
        if (costs.Any(x => x is null or <= 0)) return (Math.Round(apfd, 4), null);
        var total = costs.Sum(x => x!.Value);
        var covered = first.Values.Sum(index => costs.Skip(index).Sum(x => x!.Value) - .5 * costs[index]!.Value);
        return (Math.Round(apfd, 4), Math.Round(covered / (first.Count * total), 4));
    }
}
