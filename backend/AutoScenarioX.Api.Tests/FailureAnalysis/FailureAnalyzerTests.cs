using AutoScenarioX.Api.Services.FailureAnalysis;

namespace AutoScenarioX.Api.Tests.FailureAnalysis;

public class FailureAnalyzerTests
{
    private static readonly Guid Root = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RunA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RunB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_fault_found_by_the_first_run_scores_higher_than_the_reverse_order()
    {
        var result = FailureAnalyzer.Analyze([Fail(RunA, 1, "collision", 3), Pass(RunB, 2)]);

        Assert.Single(result.Faults);
        Assert.Equal(0.75, result.Effectiveness.Proposed.Apfd);
        Assert.Equal(0.25, result.Effectiveness.ReverseBaseline.Apfd);
        Assert.Equal(0.5, result.Effectiveness.ApfdGainVsReverse);
        Assert.Equal(2, result.Effectiveness.Proposed.Curve.Count);
        Assert.Equal(1, result.Effectiveness.Proposed.Curve[0].FaultsFound);
    }

    [Fact]
    public void The_same_check_on_variants_of_one_root_is_one_fault()
    {
        var result = FailureAnalyzer.Analyze(
        [
            Fail(RunA, 1, "red_light", 4),
            Fail(RunB, 2, "red_light", 9)
        ]);

        var fault = Assert.Single(result.Faults);
        Assert.Equal(2, fault.RunIds.Count);
        Assert.Equal(RunA, fault.FirstDetectedRunId);
        Assert.Equal(0.75, result.Effectiveness.Proposed.Apfd);
    }

    [Fact]
    public void A_warning_does_not_create_a_fault_and_a_timeout_asks_for_another_look()
    {
        var warning = new JudgedViolation("ttc", "WARNING", 2, 1, new Dictionary<string, string> { ["min_ttc_s"] = "1.2" });
        var timeout = new JudgedViolation(
            "route_incomplete", "FAILING", 92, null,
            new Dictionary<string, string> { ["reason"] = "timed_out" });
        var pass = Run(RunA, 1, "PASS", true, warning);
        var fail = Run(RunB, 2, "FAIL", false, timeout);

        var result = FailureAnalyzer.Analyze([pass, fail]);

        Assert.Equal(1, result.PassCount);
        var fault = Assert.Single(result.Faults);
        Assert.Contains("Hết giờ", fault.Explanation);
        Assert.True(fault.NeedsAdditionalVerification);
        Assert.True(result.NeedsAdditionalVerification);
    }

    [Fact]
    public void A_clone_is_its_own_root_and_a_variant_follows_its_parent()
    {
        var manual = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var variant = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var clone = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var links = new Dictionary<Guid, VersionLink>
        {
            [manual] = new(Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"), "manual", null, "SCN-00001", "Gốc"),
            [variant] = new(Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b"), "variant", manual, "SCN-00002", "Biến thể"),
            [clone] = new(Guid.Parse("cccccccc-0000-0000-0000-00000000000c"), "clone", manual, "SCN-00003", "Bản sao")
        };

        Assert.Equal(links[manual].ScenarioId, FailureAnalyzer.ResolveRoot(variant, id => links[id]));
        Assert.Equal(links[clone].ScenarioId, FailureAnalyzer.ResolveRoot(clone, id => links[id]));
    }

    [Fact]
    public void A_timeout_without_a_clear_cause_cannot_be_explained()
    {
        var timeout = new JudgedViolation(
            "route_incomplete", "FAILING", 92, null,
            new Dictionary<string, string> { ["reason"] = "timed_out" });
        var run = Run(RunA, 1, "FAIL", true, timeout);

        Assert.False(FailureAnalyzer.CanExplain(run, timeout));
    }

    [Fact]
    public void A_collision_with_video_can_be_explained()
    {
        var collision = new JudgedViolation("collision", "FAILING", 3, null, new Dictionary<string, string>());
        var run = Run(RunA, 1, "FAIL", true, collision);

        Assert.True(FailureAnalyzer.CanExplain(run, collision));
    }

    [Fact]
    public void Verify_cannot_run_before_the_faults_are_grouped()
    {
        var error = Assert.Throws<AutoScenarioX.Api.Common.Exceptions.BusinessRuleException>(
            () => AnalysisSteps.Require(AnalysisSteps.Grouped, AnalysisSteps.Verified));

        Assert.Contains("grouped", error.Message);
    }

    [Fact]
    public void Equal_costs_make_apfdc_match_apfd()
    {
        var result = FailureAnalyzer.Analyze([Fail(RunA, 1, "lane_invasion", 5), Pass(RunB, 2)]);

        Assert.Equal(result.Effectiveness.Proposed.Apfd, result.Effectiveness.Proposed.Apfdc);
    }

    private static JudgedRun Pass(Guid id, int order) => Run(id, order, "PASS", true);

    private static JudgedRun Fail(Guid id, int order, string check, double t) =>
        Run(id, order, "FAIL", true, new JudgedViolation(check, "FAILING", t, null, new Dictionary<string, string>()));

    private static JudgedRun Run(Guid id, int order, string verdict, bool video, params JudgedViolation[] violations) =>
        new(id, order, Root, "SCN-00010", "Thử", Root, "SCN-00010", "Thử", verdict, "COMPLETED", 10, video, violations);
}
