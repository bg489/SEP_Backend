using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Services.Oracle;

/// <summary>
/// Oracle offline (ADR 0001): hàm thuần Telemetry + Oracle Config -> Verdict + Violations.
/// Không I/O, không đồng hồ, không ngẫu nhiên: cùng đầu vào luôn cho cùng kết quả.
/// </summary>
public static class OracleEvaluator
{
    private static readonly HashSet<string?> RunOutcomes = ["COMPLETED", "TIMED_OUT", "STOPPED_ON_VIOLATION", "TRUNCATED"];

    private const double StillSpeedMs = 0.1;
    // shortcut: "ngay trước" = trong 15 m phía trước theo yaw của ego; chỉnh khi có route thật để xét theo waypoint.
    private const double BlockedAheadM = 15;

    public static OracleResult Evaluate(string? telemetry, OracleConfig config)
    {
        // ERROR quyết định trước mọi Check: Run hỏng hạ tầng không nói gì về SUT.
        if (telemetry is null)
            return Error("Telemetry is missing.");
        Telemetry read;
        try { read = TelemetryReader.Read(telemetry); }
        catch (TelemetryFormatException e) { return Error(e.Message); }
        static bool MarkedSynthetic(JsonNode? node)
            => node is JsonValue value && value.TryGetValue<bool>(out var marker) && marker;
        if (MarkedSynthetic(read.Header["simulated"]) || MarkedSynthetic(read.Header["synthetic"]))
            return Error("Synthetic telemetry cannot establish a driving verdict. Run the scenario in CARLA.");
        var outcome = (string?)read.End["outcome"];
        if (outcome == "INFRA_FAILED")
            return Error((string?)read.End["reason"] ?? "Infrastructure failure.");
        if (!RunOutcomes.Contains(outcome))
            return Error($"Unknown run outcome '{outcome}'.");

        // Đoạn đuôi 1 s sau khi dừng chỉ dành cho Flow 4; Oracle bỏ qua (ADR 0001).
        var data = read with
        {
            Ticks = read.Ticks.Where(r => (bool?)r["post_stop"] != true).ToList(),
            Events = read.Events.Where(r => (bool?)r["post_stop"] != true).ToList(),
        };

        var violations = Collisions(data)
            .Concat(RedLights(data))
            .Concat(LaneInvasions(data))
            .Concat(Speeding(data, config))
            .Concat(RouteIncomplete(data, config, outcome!))
            .Concat(Freezing(data, config))
            .Concat(Ttc(data, config))
            .OrderBy(v => v.T)
            .ToList();

        var verdict = violations.Any(v => v.Severity == ViolationSeverity.Failing) ? Verdict.Fail : Verdict.Pass;
        return new OracleResult(verdict, violations);
    }

    private static OracleResult Error(string reason) => new(Verdict.Error, [], reason);

    /// <summary>Mọi va chạm đều FAILING, không có ngưỡng cường độ (ADR 0001). Ai gây ra là việc của Flow 4.</summary>
    private static IEnumerable<Violation> Collisions(Telemetry data) =>
        data.Events
            .Where(e => (string?)e["type"] == "collision")
            .Select(e => new Violation("collision", ViolationSeverity.Failing, (double)e["t"]!, new JsonObject
            {
                ["other_actor"] = e["other_actor"]?.DeepClone(),
                ["impulse"] = e["impulse"]?.DeepClone(),
                ["impact_side"] = e["impact_side"]?.DeepClone(),
            }));

    /// <summary>Vượt đèn đỏ là FAILING; runner đã phát hiện bằng sensor, Oracle chỉ ghi nhận từng event.</summary>
    private static IEnumerable<Violation> RedLights(Telemetry data) =>
        data.Events
            .Where(e => (string?)e["type"] == "red_light")
            .Select(e => new Violation("red_light", ViolationSeverity.Failing, T(e), new JsonObject
            {
                ["light_id"] = e["light_id"]?.DeepClone(),
            }));

    /// <summary>Cắt vạch Solid hoặc SolidSolid là FAILING. Cắt vạch đứt thì không tính.</summary>
    private static IEnumerable<Violation> LaneInvasions(Telemetry data) =>
        data.Events
            .Where(e => (string?)e["type"] == "lane_invasion"
                        && ((JsonArray)e["markings"]!).Any(m => (string?)m is "Solid" or "SolidSolid"))
            .Select(e => new Violation("lane_invasion", ViolationSeverity.Failing, T(e), new JsonObject
            {
                ["markings"] = e["markings"]!.DeepClone(),
            }));

    /// <summary>Mỗi đoạn liên tục vượt (giới hạn + dung sai) lâu hơn ngưỡng là một Violation.</summary>
    private static IEnumerable<Violation> Speeding(Telemetry data, OracleConfig config)
    {
        var fixedLimit = Num(data.Header["route_speed_limit_kmh"]);
        double Limit(JsonObject tick) => fixedLimit ?? Num(tick["ego"]!["speed_limit_kmh"]) ?? double.PositiveInfinity;

        foreach (var seg in Segments(data.Ticks, t => Speed(t) * 3.6 - Limit(t) > config.SpeedingToleranceKmh))
        {
            if (seg.Duration <= config.SpeedingMinDurationS) continue;
            yield return new Violation("speeding", ViolationSeverity.Failing, seg.Start, new JsonObject
            {
                ["max_speed_kmh"] = Math.Round(seg.Ticks.Max(t => Speed(t) * 3.6), 2),
                ["limit_kmh"] = Limit(seg.Ticks[0]),
            }, seg.Duration);
        }
    }

    /// <summary>TIMED_OUT là chưa tới đích; COMPLETED mà ego còn xa đích hơn dung sai cũng vậy. Không có goal thì không xét.</summary>
    private static IEnumerable<Violation> RouteIncomplete(Telemetry data, OracleConfig config, string outcome)
    {
        var endT = Num(data.End["t"]) ?? 0;
        if (outcome == "TIMED_OUT")
        {
            yield return new Violation("route_incomplete", ViolationSeverity.Failing, endT, new JsonObject { ["reason"] = "timed_out" });
            yield break;
        }
        // TRUNCATED = worker cắt sớm (QUICK). Không phạt route_incomplete.
        if (outcome == "TRUNCATED" || outcome != "COMPLETED" || data.Ticks.Count == 0 || GoalDistance(data.Header, data.Ticks[^1]) is not { } d) yield break;
        if (d > config.RouteGoalToleranceM)
            yield return new Violation("route_incomplete", ViolationSeverity.Failing, endT, new JsonObject
            {
                ["reason"] = "goal_not_reached",
                ["distance_to_goal_m"] = Math.Round(d, 2),
            });
    }

    /// <summary>
    /// Đứng yên quá ngưỡng mà không có lý do: ngoài warmup, không phải đèn đỏ, chưa tới đích,
    /// không bị actor đang đứng yên chắn ngay trước trong cùng làn.
    /// </summary>
    private static IEnumerable<Violation> Freezing(Telemetry data, OracleConfig config)
    {
        var warmup = Num(data.Header["warmup_s"]) ?? 0;
        bool Frozen(JsonObject tick) =>
            Speed(tick) < StillSpeedMs
            && T(tick) >= warmup
            && (string?)tick["traffic_light"] != "red"
            && !(GoalDistance(data.Header, tick) <= config.RouteGoalToleranceM)
            && !BlockedAhead(tick);

        foreach (var seg in Segments(data.Ticks, Frozen))
        {
            if (seg.Duration <= config.FreezingMaxStillS) continue;
            yield return new Violation("freezing", ViolationSeverity.Failing, seg.Start, [], seg.Duration);
        }
    }

    /// <summary>
    /// TTC dưới ngưỡng là WARNING. Mỗi (actor, đoạn liên tục dưới ngưỡng) một Violation.
    /// shortcut: va chạm xét bằng hai hình tròn bán kính = nửa cạnh dài của bounding box, không xoay theo yaw; nâng cấp khi cần chính xác hơn.
    /// </summary>
    private static IEnumerable<Violation> Ttc(Telemetry data, OracleConfig config)
    {
        var egoRadius = Radius(data.Header["ego_extent"]);
        var open = new Dictionary<int, (double Start, double Last, double Min)>();
        foreach (var tick in data.Ticks)
        {
            var t = T(tick);
            var low = new HashSet<int>();
            foreach (var a in Actors(tick))
            {
                var id = (int)Num(a["id"])!.Value;
                if (TimeToCollision(tick["ego"]!, a, egoRadius + Radius(a["extent"])) is not { } ttc || ttc >= config.TtcThresholdS)
                    continue;
                low.Add(id);
                open[id] = open.TryGetValue(id, out var o) ? (o.Start, t, Math.Min(o.Min, ttc)) : (t, t, ttc);
            }
            foreach (var id in open.Keys.Where(k => !low.Contains(k)).ToList())
            {
                yield return TtcViolation(id, open[id]);
                open.Remove(id);
            }
        }
        foreach (var (id, o) in open) yield return TtcViolation(id, o);
    }

    private static Violation TtcViolation(int actorId, (double Start, double Last, double Min) o) =>
        new("ttc", ViolationSeverity.Warning, o.Start, new JsonObject
        {
            ["actor_id"] = actorId,
            ["min_ttc_s"] = Math.Round(o.Min, 3),
        }, o.Last - o.Start);

    /// <summary>Thời điểm sớm nhất |p + v·t| = r với p, v là vị trí và vận tốc của actor so với ego; null nếu không bao giờ chạm.</summary>
    private static double? TimeToCollision(JsonNode ego, JsonObject actor, double radius)
    {
        double px = Num(actor["x"])!.Value - Num(ego["x"])!.Value, py = Num(actor["y"])!.Value - Num(ego["y"])!.Value;
        double vx = Num(actor["vx"])!.Value - Num(ego["vx"])!.Value, vy = Num(actor["vy"])!.Value - Num(ego["vy"])!.Value;
        var c = px * px + py * py - radius * radius;
        if (c <= 0) return 0;
        var a = vx * vx + vy * vy;
        var b = 2 * (px * vx + py * vy);
        var disc = b * b - 4 * a * c;
        if (a < 1e-9 || disc < 0) return null;
        var t = (-b - Math.Sqrt(disc)) / (2 * a);
        return t >= 0 ? t : null;
    }

    private static bool BlockedAhead(JsonObject tick)
    {
        var ego = tick["ego"]!;
        var yaw = (Num(ego["yaw"]) ?? 0) * Math.PI / 180;
        foreach (var a in Actors(tick))
        {
            if (Num(a["road_id"]) != Num(ego["road_id"]) || Num(a["lane_id"]) != Num(ego["lane_id"])) continue;
            if (Math.Sqrt(Math.Pow(Num(a["vx"])!.Value, 2) + Math.Pow(Num(a["vy"])!.Value, 2)) >= StillSpeedMs) continue;
            double dx = Num(a["x"])!.Value - Num(ego["x"])!.Value, dy = Num(a["y"])!.Value - Num(ego["y"])!.Value;
            if (dx * Math.Cos(yaw) + dy * Math.Sin(yaw) > 0 && Math.Sqrt(dx * dx + dy * dy) <= BlockedAheadM) return true;
        }
        return false;
    }

    private static double? GoalDistance(JsonObject header, JsonObject tick)
    {
        if (header["goal"] is not JsonObject g) return null;
        var ego = tick["ego"]!;
        return Math.Sqrt(Math.Pow(Num(g["x"])!.Value - Num(ego["x"])!.Value, 2) + Math.Pow(Num(g["y"])!.Value - Num(ego["y"])!.Value, 2));
    }

    private sealed record Segment(IReadOnlyList<JsonObject> Ticks)
    {
        public double Start => T(Ticks[0]);
        public double Duration => T(Ticks[^1]) - Start;
    }

    /// <summary>Gom các tick liền nhau thoả điều kiện thành từng đoạn liên tục.</summary>
    private static IEnumerable<Segment> Segments(IReadOnlyList<JsonObject> ticks, Func<JsonObject, bool> match)
    {
        var run = new List<JsonObject>();
        foreach (var tick in ticks)
        {
            if (match(tick)) { run.Add(tick); continue; }
            if (run.Count > 0) yield return new Segment(run);
            run = [];
        }
        if (run.Count > 0) yield return new Segment(run);
    }

    private static IEnumerable<JsonObject> Actors(JsonObject tick) =>
        tick["actors"] is JsonArray a ? a.Cast<JsonObject>() : [];

    private static double Speed(JsonObject tick)
    {
        var ego = tick["ego"]!;
        return Math.Sqrt(Math.Pow(Num(ego["vx"])!.Value, 2) + Math.Pow(Num(ego["vy"])!.Value, 2));
    }

    private static double Radius(JsonNode? extent) => extent is null ? 0 : Math.Max(Num(extent["x"]) ?? 0, Num(extent["y"]) ?? 0);

    private static double T(JsonObject record) => (double)record["t"]!;

    private static double? Num(JsonNode? n) => n is null ? null : (double)n;
}
