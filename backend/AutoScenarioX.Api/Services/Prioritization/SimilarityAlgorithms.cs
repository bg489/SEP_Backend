namespace AutoScenarioX.Api.Services.Prioritization;

/// <summary>Bounded v1 comparison: mixed numeric/nominal Gower and exact route-point DTW.</summary>
public static class SimilarityAlgorithms
{
    public sealed record MixedFeatures(double[] Numeric, string[] Nominal);
    public sealed record RoutePoint(double X, double Y, double Z);

    public static double Gower(MixedFeatures a, MixedFeatures b, IReadOnlyList<MixedFeatures> population)
    {
        if (a.Numeric.Length != b.Numeric.Length || a.Nominal.Length != b.Nominal.Length)
            throw new ArgumentException("Feature dimensions must match.");
        double sum = 0;
        var dimensions = a.Numeric.Length + a.Nominal.Length;
        if (dimensions == 0) return 0;
        for (var i = 0; i < a.Numeric.Length; i++)
        {
            var range = population.Max(x => x.Numeric[i]) - population.Min(x => x.Numeric[i]);
            sum += range > 0 ? Math.Abs(a.Numeric[i] - b.Numeric[i]) / range : 0;
        }
        for (var i = 0; i < a.Nominal.Length; i++)
            sum += string.Equals(a.Nominal[i], b.Nominal[i], StringComparison.Ordinal) ? 0 : 1;
        return Math.Clamp(sum / dimensions, 0, 1);
    }

    public static double Dtw(IReadOnlyList<RoutePoint> a, IReadOnlyList<RoutePoint> b)
    {
        if (a.Count == 0 || b.Count == 0) throw new ArgumentException("DTW requires two nonempty routes.");
        if (a.Count > 256 || b.Count > 256) throw new ArgumentException("DTW supports at most 256 route points.");
        var previous = Enumerable.Repeat(double.PositiveInfinity, b.Count + 1).ToArray();
        previous[0] = 0;
        foreach (var p in a)
        {
            var current = Enumerable.Repeat(double.PositiveInfinity, b.Count + 1).ToArray();
            for (var j = 1; j <= b.Count; j++)
            {
                var q = b[j - 1];
                var squared = Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2) + Math.Pow(p.Z - q.Z, 2);
                current[j] = squared + Math.Min(previous[j], Math.Min(current[j - 1], previous[j - 1]));
            }
            previous = current;
        }
        return Math.Sqrt(previous[b.Count]);
    }

    /// <summary>Deterministic connected components at a distance threshold; deliberately not labeled HDBSCAN.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> Components(double[,] distance, double threshold)
    {
        if (!double.IsFinite(threshold) || threshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(threshold));
        var seen = new HashSet<int>();
        var result = new List<IReadOnlyList<int>>();
        for (var start = 0; start < distance.GetLength(0); start++)
        {
            if (!seen.Add(start)) continue;
            var members = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var next))
            {
                members.Add(next);
                for (var j = 0; j < distance.GetLength(1); j++)
                    if (distance[next, j] <= threshold && seen.Add(j)) queue.Enqueue(j);
            }
            result.Add(members.OrderBy(x => x).ToList());
        }
        return result;
    }
}
