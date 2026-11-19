using System.Globalization;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;

/// <summary>
/// Vị trí khai báo bằng toạ độ (x, y) phải nằm trên map: so với vùng do spawn point của map phủ
/// (hình chữ nhật bao các spawn point). Cách vùng đó hơn 50 m là LỖI (toạ độ của map khác hoặc gõ nhầm).
/// Nằm trong vùng nhưng cách mọi spawn point hơn 50 m chỉ CẢNH BÁO: spawn point phủ hầu hết làn lái
/// nhưng không phải mọi con đường, nên không đủ chắc để chặn publish.
/// Metadata không có hình học đường; kiểm tra trên làn thật cần CARLA (Map.get_waypoint), xem docs.
/// </summary>
public sealed class MapPositionRule : IScenarioValidationRule
{
    /// <summary>Khoảng cách tối đa ra ngoài vùng spawn point trước khi coi là nằm ngoài map.</summary>
    public const double OffMapMarginM = 50;

    /// <summary>Khoảng cách tới spawn point gần nhất mà từ đó vị trí có thể không nằm trên đường.</summary>
    public const double FarFromRoadM = 50;

    public ValidationTier Tier => ValidationTier.Carla;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        if (context.Map is not { } map) return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);

        var points = ScenarioGeometry.SpawnPoints(map);
        if (points.Count == 0) return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
        var parameters = ScenarioContentReader.Parameters(context.Normalized);
        var spawns = points.Values.Select(p => new ScenarioPosition(p.X, p.Y, p.Z, null)).ToList();
        var (minX, maxX) = (spawns.Min(p => p.X), spawns.Max(p => p.X));
        var (minY, maxY) = (spawns.Min(p => p.Y), spawns.Max(p => p.Y));

        foreach (var (path, subject, location) in ScenarioGeometry.Locations(context.Normalized))
        {
            if (!ScenarioGeometry.TryResolve(location, parameters, points, out var position) || position.SpawnIndex is not null)
                continue;   // spawn point thì đương nhiên nằm trên map

            var outside = Math.Max(Math.Max(minX - position.X, position.X - maxX), Math.Max(minY - position.Y, position.Y - maxY));
            if (outside > OffMapMarginM)
            {
                issues.Add(ValidationIssue.Error(Tier, path, string.Create(CultureInfo.InvariantCulture,
                    $"{subject} is {ScenarioGeometry.Round1(outside)} m outside map '{map.MapName}', beyond the area covered by its spawn points.")));
                continue;
            }

            var nearest = spawns.Min(p => ScenarioGeometry.Distance2D(position, p));
            if (nearest > FarFromRoadM)
                issues.Add(ValidationIssue.Warning(Tier, path, string.Create(CultureInfo.InvariantCulture,
                    $"{subject} is {ScenarioGeometry.Round1(nearest)} m from the nearest spawn point of map '{map.MapName}' and may not be on a road. Check the coordinates against the map.")));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
