using AutoScenarioX.Api.Services.Prioritization;

namespace AutoScenarioX.Api.Tests.Prioritization;

public sealed class SimilarityAlgorithmsTests
{
    [Fact]
    public void Gower_mixed_features_matches_hand_computed_example()
    {
        var a = new SimilarityAlgorithms.MixedFeatures([10, 3], ["Town01", "rain"]);
        var b = new SimilarityAlgorithms.MixedFeatures([30, 3], ["Town01", "clear"]);
        // Four equal-weight dimensions: (|10-30|/20 + 0 + 0 + 1) / 4 = .5.
        Assert.Equal(.5, SimilarityAlgorithms.Gower(a, b, [a, b]), 12);
        Assert.Equal(.5, SimilarityAlgorithms.Gower(b, a, [a, b]), 12);
        Assert.Equal(0, SimilarityAlgorithms.Gower(a, a, [a, b]));
    }

    [Fact]
    public void Dtw_matches_independent_small_grid_and_handles_time_stretch()
    {
        SimilarityAlgorithms.RoutePoint P(double x) => new(x, 0, 0);
        Assert.Equal(0, SimilarityAlgorithms.Dtw([P(0), P(1), P(2)], [P(0), P(1), P(1), P(2)]));
        // The best alignment of [0,1] and [0,2] has squared cost 1.
        Assert.Equal(1, SimilarityAlgorithms.Dtw([P(0), P(1)], [P(0), P(2)]));
        Assert.Equal(Math.Sqrt(2), SimilarityAlgorithms.Dtw([P(0), P(0)], [P(1), P(1)]), 12);
        Assert.Throws<ArgumentException>(() => SimilarityAlgorithms.Dtw([], [P(0)]));
    }

    [Fact]
    public void Threshold_components_are_deterministic_include_singletons_and_connect_transitively()
    {
        double[,] distances = { { 0, .1, .8, 1 }, { .1, 0, .2, 1 }, { .8, .2, 0, 1 }, { 1, 1, 1, 0 } };
        var clusters = SimilarityAlgorithms.Components(distances, .25);
        Assert.Equal(2, clusters.Count);
        Assert.Equal([0, 1, 2], clusters[0]);
        Assert.Equal([3], clusters[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => SimilarityAlgorithms.Components(distances, double.NaN));
    }
}
