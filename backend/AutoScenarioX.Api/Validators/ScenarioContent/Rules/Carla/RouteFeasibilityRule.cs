using System.Globalization;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;

/// <summary>
/// Route phải chạy được trong giới hạn của chính scenario, đo trên vị trí thật của spawn point:
/// 1. Không có waypoint mà goal nằm trong goal_tolerance_m quanh start (3D) thì route completion
///    thoả mãn ngay khi bắt đầu: LỖI.
/// 2. Đường thẳng qua các điểm route (2D) là cận dưới của quãng đường thật; chia cho speed_limit_kmh
///    (giới hạn Oracle dùng để bắt lỗi chạy quá tốc độ) ra thời gian tối thiểu. Lâu hơn timeout_s thì chỉ
///    run chạy quá tốc độ mới về đích được: LỖI. Quá nửa timeout: CẢNH BÁO, vì đường thật dài hơn và có dừng.
/// Không khai báo speed_limit_kmh thì Oracle dùng biển báo của map, không đủ dữ liệu để kiểm tra mục 2.
/// ego_vehicle.target_speed_kmh (tốc độ thử, runner giới hạn SUT ở mức này) thấp hơn speed limit hoặc không có speed
/// limit thì tính theo target speed: ego không chạy nhanh hơn được.
/// </summary>
public sealed class RouteFeasibilityRule : IScenarioValidationRule
{
    public const double TightTimeoutRatio = 0.5;

    public ValidationTier Tier => ValidationTier.Carla;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        if (context.Map is not { } map || context.Normalized["route"] is not JsonObject route)
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var points = ScenarioGeometry.SpawnPoints(map);
        var parameters = ScenarioContentReader.Parameters(context.Normalized);
        var waypointNodes = route["waypoints"] as JsonArray ?? [];
        var chain = new List<ScenarioPosition>();
        foreach (var node in new[] { route["start"] }.Concat(waypointNodes).Append(route["goal"]))
        {
            if (!ScenarioGeometry.TryResolve(node, parameters, points, out var position))
                return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);   // vị trí sai đã có luật khác báo
            chain.Add(position);
        }

        if (waypointNodes.Count == 0 &&
            ScenarioContentReader.TryGetNumber(route["goal_tolerance_m"], parameters, out var tolerance))
        {
            var gap = ScenarioGeometry.Distance3D(chain[0], chain[^1]);
            if (gap <= tolerance)
                issues.Add(ValidationIssue.Error(Tier, "/route/goal_tolerance_m", string.Create(CultureInfo.InvariantCulture,
                    $"The goal is {ScenarioGeometry.Round1(gap)} m from the start, within the {tolerance} m goal tolerance, so route completion would be met as soon as the run starts.")));
        }

        var hasLimit = ScenarioContentReader.TryGetNumber(route["speed_limit_kmh"], parameters, out var limit) && limit > 0;
        var target = 0.0;
        var hasTarget = context.Normalized["ego_vehicle"] is JsonObject ego &&
            ScenarioContentReader.TryGetNumber(ego["target_speed_kmh"], parameters, out target) && target > 0;
        var byTarget = hasTarget && (!hasLimit || target < limit);
        var speed = byTarget ? target : limit;
        var (label, tail) = byTarget
            ? ("ego target speed", "Only a run faster than the target speed could finish.")
            : ("speed limit", "Only a run that breaks the speed limit could finish.");
        if ((hasLimit || hasTarget) &&
            context.Normalized["execution"] is JsonObject execution &&
            ScenarioContentReader.TryGetNumber(execution["timeout_s"], parameters, out var timeout))
        {
            var length = 0.0;
            for (var i = 1; i < chain.Count; i++) length += ScenarioGeometry.Distance2D(chain[i - 1], chain[i]);
            var minimum = length / (speed / 3.6);
            var (time, distance) = (ScenarioGeometry.Round1(minimum), ScenarioGeometry.Round1(length));

            if (minimum > timeout)
                issues.Add(ValidationIssue.Error(Tier, "/execution/timeout_s", string.Create(CultureInfo.InvariantCulture,
                    $"Even in a straight line at the {speed} km/h {label}, the ego needs at least {time} s to cover {distance} m between the route points, but the run times out after {timeout} s. {tail}")));
            else if (minimum > timeout * TightTimeoutRatio)
                issues.Add(ValidationIssue.Warning(Tier, "/execution/timeout_s", string.Create(CultureInfo.InvariantCulture,
                    $"In a straight line at the {speed} km/h {label}, the ego already needs {time} s of the {timeout} s timeout to cover {distance} m between the route points. Real routes are longer and include stops, so the run may time out.")));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
