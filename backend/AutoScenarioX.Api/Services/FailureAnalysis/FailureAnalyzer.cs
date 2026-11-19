using AutoScenarioX.Api.DTOs.FailureAnalysis;

namespace AutoScenarioX.Api.Services.FailureAnalysis;

/// <summary>Một run đã có Evaluation. ERROR không nói gì về SUT nên không vào APFD.</summary>
public sealed record JudgedRun(
    Guid RunId,
    int SortOrder,
    Guid ScenarioId,
    string ScenarioCode,
    string ScenarioName,
    Guid RootScenarioId,
    string RootScenarioCode,
    string RootScenarioName,
    string Verdict,
    string? Outcome,
    double? DurationS,
    bool HasVideo,
    IReadOnlyList<JudgedViolation> Violations);

public sealed record JudgedViolation(
    string Check,
    string Severity,
    double T,
    double? DurationS,
    IReadOnlyDictionary<string, string> Details);

public sealed record VersionLink(Guid ScenarioId, string SourceType, Guid? ParentVersionId, string ScenarioCode, string ScenarioName);

/// <summary>
/// Phân tích lỗi từ kết quả Oracle đã lưu. Fault = (root scenario, check) của vi phạm FAILING.
/// APFD đếm fault đó, không đếm từng run.
/// </summary>
public static class FailureAnalyzer
{
    public const string Failing = "FAILING";

    public static Guid ResolveRoot(Guid startVersionId, Func<Guid, VersionLink?> lookup)
    {
        var current = startVersionId;
        var seen = new HashSet<Guid>();
        Guid fallback = startVersionId;
        while (lookup(current) is { } link && seen.Add(current))
        {
            fallback = link.ScenarioId;
            var variant = string.Equals(link.SourceType, "variant", StringComparison.OrdinalIgnoreCase);
            if (!variant || link.ParentVersionId is not Guid parent)
                return link.ScenarioId;
            current = parent;
        }
        return fallback;
    }

    public static AnalysisCore Analyze(IReadOnlyList<JudgedRun> runs, int baselineSeed = 42)
    {
        var ordered = runs.OrderBy(r => r.SortOrder).ThenBy(r => r.RunId).ToList();
        var pass = ordered.Count(r => r.Verdict == "PASS");
        var fail = ordered.Count(r => r.Verdict == "FAIL");
        var suite = ordered.Where(r => r.Verdict is "PASS" or "FAIL").ToList();
        var faults = GroupFaults(suite);
        var features = ordered.Select(ToFeature).ToList();
        var proposed = Score("proposed", suite, faults);
        var reverse = Score("reverse", suite.AsEnumerable().Reverse().ToList(), faults);
        var randomOrder = Shuffle(suite, baselineSeed);
        var random = Score("random", randomOrder, faults);
        double? gain = proposed.Apfd is { } p && reverse.Apfd is { } b ? Math.Round(p - b, 4) : null;
        var needs = faults.Any(f => f.NeedsAdditionalVerification);
        return new AnalysisCore(
            ordered.Count, pass, fail, features, faults,
            new EffectivenessResponse(proposed, reverse, random, gain),
            needs,
            Summarize(ordered.Count, pass, fail, faults.Count, needs));
    }

    /// <summary>
    /// Không có video, hoặc chỉ hết giờ chưa tới đích, thì chưa đủ căn cứ để giải thích.
    /// </summary>
    public static bool CanExplain(JudgedRun run, JudgedViolation violation)
    {
        if (!run.HasVideo)
            return false;
        return violation.Check != "route_incomplete"
               || !violation.Details.TryGetValue("reason", out var reason)
               || reason != "timed_out";
    }

    public static string Explain(JudgedViolation violation)
    {
        var d = violation.Details;
        string Get(string key) => d.TryGetValue(key, out var v) ? v : "";
        return violation.Check switch
        {
            "collision" =>
                $"Xe va chạm lúc {violation.T:0.##}s, phía {Or(Get("impact_side"), "không rõ")}, đối tượng {Or(Get("other_actor.type"), "không rõ")}.",
            "red_light" =>
                $"Xe chạy xuyên đèn đỏ lúc {violation.T:0.##}s.",
            "speeding" =>
                $"Vượt tốc độ từ {violation.T:0.##}s, tối đa {Or(Get("max_speed_kmh"), "?")} km/h, giới hạn {Or(Get("limit_kmh"), "?")} km/h, kéo dài {Span(violation)}.",
            "route_incomplete" when Get("reason") == "timed_out" =>
                $"Hết giờ lúc {violation.T:0.##}s mà xe chưa tới đích.",
            "route_incomplete" =>
                $"Chưa tới đích lúc {violation.T:0.##}s, còn cách {Or(Get("distance_to_goal_m"), "?")} m.",
            "freezing" =>
                $"Đứng yên không có lý do từ {violation.T:0.##}s, kéo dài {Span(violation)}.",
            "lane_invasion" =>
                $"Cắt vạch {Or(Get("markings"), "liền")} lúc {violation.T:0.##}s.",
            "ttc" =>
                $"Suýt va chạm từ {violation.T:0.##}s, thời gian tới va chạm nhỏ nhất {Or(Get("min_ttc_s"), "?")}s.",
            _ => $"Vi phạm {violation.Check} lúc {violation.T:0.##}s."
        };
    }

    private static List<FaultGroupResponse> GroupFaults(IReadOnlyList<JudgedRun> suite)
    {
        var groups = new Dictionary<(Guid Root, string Check), List<(JudgedRun Run, JudgedViolation Violation)>>();
        foreach (var run in suite)
        {
            foreach (var violation in run.Violations.Where(v => v.Severity == Failing))
            {
                var key = (run.RootScenarioId, violation.Check);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = [];
                list.Add((run, violation));
            }
        }

        return groups
            .Select(pair =>
            {
                var hits = pair.Value.OrderBy(x => x.Run.SortOrder).ThenBy(x => x.Violation.T).ToList();
                var first = hits[0];
                var needs = hits.Any(h => !h.Run.HasVideo)
                    || (first.Violation.Check == "route_incomplete"
                        && first.Violation.Details.TryGetValue("reason", out var reason)
                        && reason == "timed_out");
                var explanation = Explain(first.Violation);
                var codes = string.Join(", ", hits.Select(h => h.Run.ScenarioCode).Distinct());
                var report =
                    $"{first.Run.RootScenarioName} ({first.Run.RootScenarioCode}), rule {first.Violation.Check}: {explanation} " +
                    $"Thấy ở {hits.Count} lượt ({codes}).";
                var runIds = hits.Select(h => h.Run.RunId).Distinct().ToList();
                return new FaultGroupResponse(
                    first.Run.RootScenarioId,
                    first.Run.RootScenarioCode,
                    first.Run.RootScenarioName,
                    first.Violation.Check,
                    runIds,
                    first.Run.RunId,
                    first.Violation.T,
                    explanation,
                    report,
                    needs,
                    needs ? AnalysisSteps.AwaitingFlow3 : AnalysisSteps.Explainable,
                    runIds.Skip(1).ToList());
            })
            .OrderBy(f => f.FirstT)
            .ThenBy(f => f.Check, StringComparer.Ordinal)
            .ToList();
    }

    private static OrderEffectivenessResponse Score(string label, IReadOnlyList<JudgedRun> order, IReadOnlyList<FaultGroupResponse> faults)
    {
        var n = order.Count;
        var m = faults.Count;
        if (n == 0 || m == 0)
            return new OrderEffectivenessResponse(label, null, null, []);

        var positions = new List<int>(m);
        var costs = order.Select(r => r.DurationS is > 0 ? r.DurationS.Value : 1d).ToArray();
        foreach (var fault in faults)
        {
            var index = -1;
            for (var i = 0; i < order.Count; i++)
            {
                if (fault.RunIds.Contains(order[i].RunId))
                {
                    index = i;
                    break;
                }
            }
            if (index >= 0)
                positions.Add(index + 1);
        }
        if (positions.Count == 0)
            return new OrderEffectivenessResponse(label, null, null, []);

        var apfd = 1d - positions.Sum() / (double)(n * positions.Count) + 1d / (2d * n);
        var total = costs.Sum();
        double covered = 0;
        foreach (var tf in positions)
        {
            double suffix = 0;
            for (var j = tf - 1; j < costs.Length; j++)
                suffix += costs[j];
            covered += suffix - 0.5 * costs[tf - 1];
        }
        var apfdc = covered / (positions.Count * total);

        var found = new HashSet<int>();
        var curve = new List<DetectionPointResponse>();
        for (var i = 0; i < order.Count; i++)
        {
            for (var f = 0; f < faults.Count; f++)
            {
                if (faults[f].RunIds.Contains(order[i].RunId))
                    found.Add(f);
            }
            curve.Add(new DetectionPointResponse(i + 1, found.Count, Math.Round(found.Count / (double)m, 4)));
        }

        return new OrderEffectivenessResponse(label, Math.Round(apfd, 4), Math.Round(apfdc, 4), curve);
    }

    private static RunFeatureResponse ToFeature(JudgedRun run) => new(
        run.RunId,
        run.SortOrder,
        run.ScenarioCode,
        run.ScenarioName,
        run.Verdict,
        run.Outcome,
        run.DurationS,
        run.HasVideo,
        run.Violations.Where(v => v.Severity == Failing).Select(v => v.Check).Distinct().ToList(),
        run.Violations.Where(v => v.Severity != Failing).Select(v => v.Check).Distinct().ToList());

    private static List<JudgedRun> Shuffle(IReadOnlyList<JudgedRun> runs, int seed)
    {
        var copy = runs.ToList();
        var rng = new Random(seed);
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    private static string Summarize(int judged, int pass, int fail, int faults, bool needs) =>
        judged == 0
            ? "Chưa có kết quả Oracle để phân tích."
            : $"Đã chấm {judged} lượt: {pass} PASS, {fail} FAIL, {faults} fault. " +
              (needs
                  ? "Có fault cần xem video hoặc chạy thêm trước khi kết luận."
                  : "Các fault đều có bằng chứng để duyệt.");

    private static string Span(JudgedViolation violation) =>
        violation.DurationS is { } d ? $"{d:0.##}s" : "không rõ";

    private static string Or(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}

public sealed record AnalysisCore(
    int RunsJudged,
    int PassCount,
    int FailCount,
    IReadOnlyList<RunFeatureResponse> Features,
    IReadOnlyList<FaultGroupResponse> Faults,
    EffectivenessResponse Effectiveness,
    bool NeedsAdditionalVerification,
    string Summary);
