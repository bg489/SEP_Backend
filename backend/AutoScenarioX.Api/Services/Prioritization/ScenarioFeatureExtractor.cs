using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Services.Prioritization;

/// <summary>Trích xuất vector đặc trưng số từ content + cột generated của version.</summary>
public static class ScenarioFeatureExtractor
{
    public static readonly IReadOnlyList<string> FeatureNames =
    [
        .. ScenarioCatalog.Categories.Select(c => $"cat_{c}"),
        "weather_severity",
        "npc_vehicles",
        "pedestrians",
        "actor_count",
        "event_count",
        .. ScenarioCatalog.EventTypes.Select(e => $"evt_{e}"),
        "speed_limit_kmh",
        "ego_initial_speed_kmh",
        "has_parameters",
        "is_variant"
    ];

    public sealed record ExtractedFeatures(
        double[] Vector,
        string Severity,
        int FailureSignalEstimate,
        double EstimatedCostMinutes,
        string RationaleHints);

    public static ExtractedFeatures Extract(ScenarioVersion version, Scenario scenario)
    {
        var vector = new double[FeatureNames.Count];
        var i = 0;

        foreach (var category in ScenarioCatalog.Categories)
            vector[i++] = string.Equals(scenario.Category, category, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        var weatherSeverity = WeatherSeverity(version.WeatherPreset);
        vector[i++] = weatherSeverity;
        vector[i++] = version.NpcVehicleCount ?? 0;
        vector[i++] = version.PedestrianCount ?? 0;
        vector[i++] = version.ActorCount;
        vector[i++] = version.EventCount;

        var eventTypes = ParseEventTypes(version.EventTypes);
        foreach (var eventType in ScenarioCatalog.EventTypes)
            vector[i++] = eventTypes.Contains(eventType) ? 1 : 0;

        vector[i++] = (double)(version.SpeedLimitKmh ?? 0);
        var egoSpeed = ReadEgoInitialSpeed(version.Content);
        vector[i++] = egoSpeed;
        vector[i++] = ContentHasParameters(version.Content) ? 1 : 0;
        vector[i++] = string.Equals(scenario.OriginType, ScenarioOriginTypes.Variant, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        var risk = ComputeRisk(scenario.Category, weatherSeverity, version.EventCount, version.ActorCount, eventTypes);
        var severity = risk >= 75 ? "Critical" : risk >= 55 ? "High" : risk >= 35 ? "Medium" : "Low";
        var signals = (int)Math.Clamp(Math.Round(risk / 12.0 + version.EventCount), 0, 20);
        var cost = 4.0 + version.ActorCount * 0.48 + version.EventCount * 0.35 + (version.NpcVehicleCount ?? 0) * 0.05;
        var hints = BuildHints(scenario.Category, version.WeatherPreset, eventTypes, weatherSeverity);

        return new ExtractedFeatures(vector, severity, signals, Math.Round(cost, 1), hints);
    }

    public static double[][] MinMaxNormalize(IReadOnlyList<double[]> rows)
    {
        if (rows.Count == 0) return [];
        var dim = rows[0].Length;
        var mins = new double[dim];
        var maxs = new double[dim];
        Array.Fill(mins, double.PositiveInfinity);
        Array.Fill(maxs, double.NegativeInfinity);

        foreach (var row in rows)
        {
            for (var d = 0; d < dim; d++)
            {
                mins[d] = Math.Min(mins[d], row[d]);
                maxs[d] = Math.Max(maxs[d], row[d]);
            }
        }

        var result = new double[rows.Count][];
        for (var r = 0; r < rows.Count; r++)
        {
            result[r] = new double[dim];
            for (var d = 0; d < dim; d++)
            {
                var range = maxs[d] - mins[d];
                result[r][d] = range < 1e-9 ? 0 : (rows[r][d] - mins[d]) / range;
            }
        }
        return result;
    }

    public static double[,] DistanceMatrix(IReadOnlyList<double[]> normalized)
    {
        var n = normalized.Count;
        var matrix = new double[n, n];
        for (var a = 0; a < n; a++)
        for (var b = 0; b < n; b++)
        {
            if (a == b) { matrix[a, b] = 0; continue; }
            if (b < a) { matrix[a, b] = matrix[b, a]; continue; }
            matrix[a, b] = Euclidean(normalized[a], normalized[b]);
        }
        return matrix;
    }

    private static double Euclidean(double[] a, double[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }

    private static double WeatherSeverity(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return 0.1;
        if (preset.Contains("HardRain", StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (preset.Contains("MidRain", StringComparison.OrdinalIgnoreCase)) return 0.75;
        if (preset.Contains("SoftRain", StringComparison.OrdinalIgnoreCase)) return 0.55;
        if (preset.Contains("Wet", StringComparison.OrdinalIgnoreCase)) return 0.45;
        if (preset.Contains("Cloudy", StringComparison.OrdinalIgnoreCase)) return 0.25;
        return 0.1;
    }

    private static double ComputeRisk(
        string category,
        double weatherSeverity,
        int eventCount,
        int actorCount,
        HashSet<string> eventTypes)
    {
        double baseRisk = category switch
        {
            "cut_in" or "braking" or "pedestrian" => 70,
            "traffic_light" or "intersection" or "obstacle" => 55,
            "following" => 40,
            _ => 30
        };
        baseRisk += weatherSeverity * 20;
        baseRisk += Math.Min(eventCount, 5) * 4;
        baseRisk += Math.Min(actorCount, 8) * 1.5;
        if (eventTypes.Contains("lead_vehicle_brake")) baseRisk += 6;
        if (eventTypes.Contains("vehicle_cut_in")) baseRisk += 6;
        return Math.Clamp(baseRisk, 0, 100);
    }

    private static HashSet<string> ParseEventTypes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static double ReadEgoInitialSpeed(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("ego_vehicle", out var ego) &&
                ego.TryGetProperty("initial_speed_kmh", out var speed) &&
                speed.ValueKind == JsonValueKind.Number)
                return speed.GetDouble();
        }
        catch { /* ignore malformed */ }
        return 0;
    }

    private static bool ContentHasParameters(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("parameters", out var parameters)) return false;
            return parameters.ValueKind == JsonValueKind.Object && parameters.EnumerateObject().Any();
        }
        catch
        {
            return false;
        }
    }

    private static string BuildHints(string category, string? weather, HashSet<string> events, double weatherSeverity)
    {
        var parts = new List<string> { $"category:{category}" };
        if (!string.IsNullOrWhiteSpace(weather)) parts.Add($"weather:{weather}");
        if (weatherSeverity >= 0.55) parts.Add("adverse weather");
        if (events.Count > 0) parts.Add("events:" + string.Join("+", events.Take(3)));
        return string.Join(" · ", parts);
    }
}
