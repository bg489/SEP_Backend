using AutoScenarioX.Api.DTOs.Prioritization;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Prioritization;

namespace AutoScenarioX.Api.Tests.Prioritization;

public sealed class PrioritizationEngineTests
{
    private static PrioritizationEngine.CandidateItem Item(int i, string severity = "Low", VersionFailureHistory? history = null)
        => new(i, new Guid(i + 1, 0, 0, new byte[8]), new([i], severity, i, 1, "fixture"), [i / 10d], history);

    [Fact]
    public void Failure_history_uses_observed_oracle_failures_even_when_content_risk_is_lower()
    {
        var items = new[] { Item(0, "Critical", new(Guid.NewGuid(), 10, 0, 1, [])),
            Item(1, "Low", new(Guid.NewGuid(), 10, 8, 1, ["root:collision"])) };
        var result = PrioritizationEngine.Rank(items, PrioritizationStrategies.ProposedFailureHistory,
            new PrioritizationParams { DiversityWeight = 0, RiskWeight = 1 });
        Assert.Equal([1, 0], result.Order);
        Assert.Contains("8/10 FAIL", result.Rationales[1]);
        Assert.Equal("complete", result.Metrics.HistoryStatus);
        Assert.Equal(.75, result.Metrics.ObservedApfd);
    }

    [Fact]
    public void No_history_is_explicit_and_observed_metrics_are_unavailable()
    {
        var result = PrioritizationEngine.Rank([Item(0)], PrioritizationStrategies.ProposedFailureHistory, null);
        Assert.Equal([0], result.Order);
        Assert.Equal("no_data", result.Metrics.HistoryStatus);
        Assert.Null(result.Metrics.ObservedApfd);
        Assert.Contains("No judged Oracle history", result.Metrics.Note);
        Assert.Contains("fallback", result.Rationales[0]);
    }

    [Fact]
    public void Random_seed_is_repeatable_and_equal_scores_are_stable()
    {
        var items = Enumerable.Range(0, 20).Select(i => Item(i)).ToList();
        var options = new PrioritizationParams { Seed = 121 };
        var one = PrioritizationEngine.Rank(items, PrioritizationStrategies.RandomBaseline, options);
        var two = PrioritizationEngine.Rank(items, PrioritizationStrategies.RandomBaseline, options);
        Assert.Equal(one.Order, two.Order);
        Assert.Equal(20, one.Order.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 20), PrioritizationEngine.Rank(items, PrioritizationStrategies.RiskFirst, null).Order);
    }

    [Fact]
    public void Repeated_fault_counts_once_and_missing_duration_does_not_fabricate_cost_metric()
    {
        var items = new[] { Item(0, history: new(Guid.NewGuid(), 3, 3, null, ["same", "same"])),
            Item(1, history: new(Guid.NewGuid(), 1, 1, 1, ["same"])) };
        var metrics = PrioritizationEngine.ObservedMetrics([0, 1], items);
        Assert.Equal(.75, metrics.Apfd);
        Assert.Null(metrics.Apfdc);
    }

    [Fact]
    public void Empty_input_is_rejected_and_constant_features_normalize_without_nan()
    {
        Assert.Throws<ArgumentException>(() => PrioritizationEngine.Rank([], PrioritizationStrategies.Diversity, null));
        var normalized = ScenarioFeatureExtractor.MinMaxNormalize([[7, 4], [7, 4]]);
        Assert.All(normalized, row => Assert.All(row, x => Assert.Equal(0, x)));
    }
}
