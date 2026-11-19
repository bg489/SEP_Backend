using System.Text.Json;
using AutoScenarioX.Api.Services.Oracle;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Oracle;

/// <summary>
/// Seam A (ADR 0001): Oracle là hàm thuần, chạy trên file Telemetry mẫu trong examples/telemetry/.
/// Các file mẫu đồng thời là hợp đồng asx.telemetry/1 mà runner (SV2) phải ghi đúng.
/// </summary>
public class OracleEvaluatorTests
{
    private static OracleResult Evaluate(string fixture) =>
        OracleEvaluator.Evaluate(File.ReadAllText(TestData.TelemetryPath(fixture)), OracleConfig.V1);

    [Fact]
    public void Clean_run_passes()
    {
        var result = Evaluate("clean_pass.jsonl");

        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Collision_of_any_intensity_fails_even_when_rear_ended()
    {
        var result = Evaluate("collision_rear_end_light.jsonl");

        Assert.Equal(Verdict.Fail, result.Verdict);
        var violation = Assert.Single(result.Violations, v => v.Check == "collision");
        Assert.Equal("collision", violation.Check);
        Assert.Equal(ViolationSeverity.Failing, violation.Severity);
        Assert.Equal(0.05, violation.T);
        Assert.Equal(12.5, (double)violation.Details["impulse"]!);
        Assert.Equal("rear", (string)violation.Details["impact_side"]!);
        Assert.Equal(205, (int)violation.Details["other_actor"]!["id"]!);
        Assert.Equal("vehicle", (string)violation.Details["other_actor"]!["type"]!);
    }

    [Fact]
    public void Post_stop_tail_is_ignored()
    {
        var result = Evaluate("collision_with_post_stop_tail.jsonl");

        var violation = Assert.Single(result.Violations, v => v.Check == "collision");
        Assert.Equal(1.0, violation.T);
        Assert.Equal("walker", (string)violation.Details["other_actor"]!["type"]!);
    }

    /// <summary>
    /// Bản ghi thật từ tools/carla_recorder.py đúng hợp đồng: chỉ chạy khi đặt ASX_TELEMETRY_DIR
    /// (không commit bản ghi, nên mặc định bỏ qua để dotnet test vẫn offline).
    /// </summary>
    [CarlaTelemetryFact]
    public void Real_carla_recordings_are_judged_without_format_error()
    {
        var dir = Environment.GetEnvironmentVariable("ASX_TELEMETRY_DIR");
        Assert.False(string.IsNullOrEmpty(dir));
        var files = Directory.GetFiles(dir!, "*.jsonl");
        Assert.NotEmpty(files);
        Assert.All(files, file =>
        {
            var text = File.ReadAllText(file);
            var outcome = (string?)TelemetryReader.Read(text).End["outcome"];
            var result = OracleEvaluator.Evaluate(text, OracleConfig.V1);
            Assert.True(result.Verdict != Verdict.Error || outcome == "INFRA_FAILED", $"{file}: {result.ErrorReason}");
        });
    }

    [Fact]
    public void Red_light_event_fails()
    {
        var result = Evaluate("red_light.jsonl");

        Assert.Equal(Verdict.Fail, result.Verdict);
        var v = Assert.Single(result.Violations);
        Assert.Equal("red_light", v.Check);
        Assert.Equal(1.0, v.T);
        Assert.Equal(77, (int)v.Details["light_id"]!);
    }

    [Fact]
    public void Lane_invasion_of_a_solid_line_fails_and_a_broken_line_does_not_count()
    {
        var result = Evaluate("lane_invasion_solid.jsonl");

        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Equal([1.0, 1.5], result.Violations.Select(v => v.T));
        Assert.All(result.Violations, v =>
        {
            Assert.Equal("lane_invasion", v.Check);
            Assert.Equal(ViolationSeverity.Failing, v.Severity);
        });
    }

    [Fact]
    public void Speeding_stretch_is_one_violation_with_duration()
    {
        var result = Evaluate("speeding.jsonl");

        Assert.Equal(Verdict.Fail, result.Verdict);
        var v = Assert.Single(result.Violations);
        Assert.Equal("speeding", v.Check);
        Assert.Equal(1.0, v.T);
        Assert.Equal(4.0, v.DurationS);
        Assert.Equal(50, (double)v.Details["limit_kmh"]!);
    }

    [Fact]
    public void Speeding_within_tolerance_or_shorter_than_min_duration_passes()
    {
        var text = File.ReadAllText(TestData.TelemetryPath("speeding.jsonl"));

        Assert.Equal(Verdict.Pass, OracleEvaluator.Evaluate(text, OracleConfig.V1 with { SpeedingToleranceKmh = 40 }).Verdict);
        Assert.Equal(Verdict.Pass, OracleEvaluator.Evaluate(text, OracleConfig.V1 with { SpeedingMinDurationS = 4 }).Verdict);
    }

    [Fact]
    public void Timed_out_run_is_route_incomplete()
    {
        var result = Evaluate("timed_out.jsonl");

        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Equal("route_incomplete", Assert.Single(result.Violations).Check);
    }

    [Fact]
    public void Completed_run_far_from_goal_is_route_incomplete_but_near_goal_passes()
    {
        Assert.Equal(Verdict.Pass, Evaluate("goal_reached.jsonl").Verdict);

        var far = File.ReadAllText(TestData.TelemetryPath("goal_reached.jsonl")).Replace("\"goal\":{\"x\":10.0", "\"goal\":{\"x\":100.0");
        var result = OracleEvaluator.Evaluate(far, OracleConfig.V1);

        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Equal("route_incomplete", Assert.Single(result.Violations).Check);
    }

    [Fact]
    public void Freezing_ignores_warmup_and_red_light_but_flags_the_rest()
    {
        var result = Evaluate("freezing.jsonl");

        Assert.Equal(Verdict.Fail, result.Verdict);
        var v = Assert.Single(result.Violations);
        Assert.Equal("freezing", v.Check);
        Assert.Equal(12.0, v.T);
        Assert.Equal(18.0, v.DurationS);
    }

    [Fact]
    public void Freezing_behind_a_stopped_actor_ahead_is_legitimate()
    {
        var result = Evaluate("freezing_blocked.jsonl");

        Assert.Equal(Verdict.Pass, result.Verdict);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Low_ttc_is_a_warning_and_does_not_fail()
    {
        var result = Evaluate("ttc_low.jsonl");

        Assert.Equal(Verdict.Pass, result.Verdict);
        var v = Assert.Single(result.Violations);
        Assert.Equal("ttc", v.Check);
        Assert.Equal(ViolationSeverity.Warning, v.Severity);
        Assert.Equal(0.5, v.T);
        Assert.Equal(400, (int)v.Details["actor_id"]!);
        Assert.True((double)v.Details["min_ttc_s"]! < OracleConfig.V1.TtcThresholdS);
    }

    [Fact]
    public void Non_numeric_tick_field_is_error()
    {
        var text = File.ReadAllText(TestData.TelemetryPath("clean_pass.jsonl")).Replace("\"vx\":5.0", "\"vx\":\"fast\"");

        var result = OracleEvaluator.Evaluate(text, OracleConfig.V1);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Equal("Telemetry line 2: 'ego.vx' must be a number.", result.ErrorReason);
    }

    /// <summary>ERROR được quyết định trước mọi Check: dù file có va chạm, Run không nói gì về SUT.</summary>
    [Theory]
    [InlineData("infra_failed.jsonl", "CARLA server stopped responding")]
    [InlineData("truncated.jsonl", "Telemetry is truncated: no end-of-run record.")]
    [InlineData("unknown_format.jsonl", "Unsupported telemetry format 'asx.telemetry/2'.")]
    [InlineData("unknown_outcome.jsonl", "Unknown run outcome 'FINISHED'.")]
    public void Unusable_run_is_error_without_violations(string fixture, string reason)
    {
        var result = Evaluate(fixture);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Empty(result.Violations);
        Assert.Equal(reason, result.ErrorReason);
    }

    /// <summary>Tái lập được: chấm lại cùng Telemetry và cùng Oracle Config phải ra y hệt.</summary>
    [Fact]
    public void Same_input_gives_identical_result_for_every_fixture()
    {
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(TestData.TelemetryPath("x"))!, "*.jsonl"))
        {
            var fixture = Path.GetFileName(path);
            Assert.Equal(JsonSerializer.Serialize(Evaluate(fixture)), JsonSerializer.Serialize(Evaluate(fixture)));
        }
    }

    /// <summary>Giá trị sai kiểu không được làm Oracle ném lỗi: Run không chấm được thì là ERROR.</summary>
    [Theory]
    [InlineData("\"t\":0.05,\"post_stop\":false,\"type\":\"collision\"", "\"post_stop\":false,\"type\":\"collision\"", "Telemetry line 4: 't' must be a number.")]
    [InlineData("\"t\":0.05,\"post_stop\":false,\"type\":\"collision\"", "\"t\":\"late\",\"post_stop\":false,\"type\":\"collision\"", "Telemetry line 4: 't' must be a number.")]
    [InlineData("\"t\":0.05,\"post_stop\":false,\"type\":\"collision\"", "\"t\":0.05,\"post_stop\":\"no\",\"type\":\"collision\"", "Telemetry line 4: 'post_stop' must be true or false.")]
    [InlineData("\"outcome\":\"STOPPED_ON_VIOLATION\"", "\"outcome\":3", "Telemetry line 5: 'outcome' must be a string.")]
    [InlineData("\"format\":\"asx.telemetry/1\"", "\"format\":1", "Telemetry line 1: 'format' must be a string.")]
    public void Wrongly_typed_value_is_error(string valid, string broken, string reason)
    {
        var text = File.ReadAllText(TestData.TelemetryPath("collision_rear_end_light.jsonl"));
        Assert.Contains(valid, text);

        var result = OracleEvaluator.Evaluate(text.Replace(valid, broken), OracleConfig.V1);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Equal(reason, result.ErrorReason);
    }

    [Theory]
    [InlineData("null", "Telemetry line 3 is not a JSON object.")]
    [InlineData("{\"kind\":\"tick\",", "Telemetry line 3 is not valid JSON.")]
    public void Corrupt_line_in_the_middle_is_error(string line, string reason)
    {
        var lines = File.ReadAllLines(TestData.TelemetryPath("collision_rear_end_light.jsonl")).ToList();
        lines[2] = line;

        var result = OracleEvaluator.Evaluate(string.Join('\n', lines), OracleConfig.V1);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Equal(reason, result.ErrorReason);
    }

    [Fact]
    public void Missing_telemetry_is_error()
    {
        var result = OracleEvaluator.Evaluate(null, OracleConfig.V1);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Equal("Telemetry is missing.", result.ErrorReason);
    }
}
