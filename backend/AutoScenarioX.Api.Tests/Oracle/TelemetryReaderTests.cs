using System.Text.Json.Nodes;
using AutoScenarioX.Api.Services.Oracle;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Oracle;

public sealed class TelemetryReaderTests
{
    private static List<string> Clean() => File.ReadAllLines(TestData.TelemetryPath("clean_pass.jsonl")).ToList();
    private static OracleResult Judge(IEnumerable<string> lines) => OracleEvaluator.Evaluate(string.Join('\n', lines), OracleConfig.V1);

    [Theory]
    [InlineData("duplicate_header")]
    [InlineData("duplicate_end")]
    [InlineData("tick_after_end")]
    [InlineData("tick_before_header")]
    [InlineData("unknown_kind")]
    [InlineData("decreasing_tick")]
    [InlineData("no_ticks")]
    [InlineData("invalid_end_time")]
    [InlineData("negative_time")]
    [InlineData("nonfinite_time")]
    public void Malformed_record_stream_is_error_instead_of_a_driving_pass(string mutation)
    {
        var lines = Clean();
        switch (mutation)
        {
            case "duplicate_header": lines.Insert(1, lines[0]); break;
            case "duplicate_end": lines.Add(lines[^1]); break;
            case "tick_after_end": lines.Add(lines[1]); break;
            case "tick_before_header": lines.RemoveAt(0); break;
            case "unknown_kind": lines[1] = lines[1].Replace("\"kind\":\"tick\"", "\"kind\":\"frame\""); break;
            case "decreasing_tick": (lines[1], lines[2]) = (lines[2], lines[1]); break;
            case "no_ticks": lines.RemoveRange(1, 2); break;
            case "invalid_end_time": lines[^1] = lines[^1].Replace("\"t\":0.05", "\"t\":\"invalid\""); break;
            case "negative_time": lines[1] = lines[1].Replace("\"t\":0.00", "\"t\":-1"); break;
            case "nonfinite_time": lines[1] = lines[1].Replace("\"t\":0.00", "\"t\":1e309"); break;
        }

        var result = Judge(lines);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.NotNull(result.ErrorReason);
        Assert.Empty(result.Violations);
    }

    [Theory]
    [InlineData("simulated")]
    [InlineData("synthetic")]
    public void Synthetic_marker_never_produces_a_verified_driving_pass(string marker)
    {
        var lines = Clean();
        var header = JsonNode.Parse(lines[0])!;
        header[marker] = true;
        lines[0] = header.ToJsonString();

        var result = Judge(lines);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Contains("Synthetic telemetry", result.ErrorReason);
    }

    [Fact]
    public void Delayed_sensor_event_retains_its_earlier_timestamp_and_is_judged()
    {
        var lines = Clean();
        lines.Insert(lines.Count - 1, """{"kind":"event","t":0.01,"post_stop":false,"type":"collision"}""");

        var result = Judge(lines);

        Assert.Equal(Verdict.Fail, result.Verdict);
        Assert.Equal(0.01, Assert.Single(result.Violations).T);
    }

    [Fact]
    public void Failure_before_simulation_starts_remains_an_infrastructure_error()
    {
        var lines = Clean();
        lines.RemoveRange(1, 2);
        lines[^1] = """{"kind":"end","t":0,"outcome":"INFRA_FAILED","reason":"CARLA could not load the map"}""";

        var result = Judge(lines);

        Assert.Equal(Verdict.Error, result.Verdict);
        Assert.Equal("CARLA could not load the map", result.ErrorReason);
    }
}
