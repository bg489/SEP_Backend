using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Services.Oracle;

public enum Verdict { Pass, Fail, Error }

public enum ViolationSeverity { Failing, Warning }

/// <summary>Một Check bị vi phạm tại một thời điểm mô phỏng <paramref name="T"/> (giây); <paramref name="DurationS"/> cho Check kéo dài (Speeding, Freezing, TTC).</summary>
public sealed record Violation(string Check, ViolationSeverity Severity, double T, JsonObject Details, double? DurationS = null);

/// <summary>Kết quả một lần chấm. <paramref name="ErrorReason"/> chỉ có khi Verdict = Error.</summary>
public sealed record OracleResult(Verdict Verdict, IReadOnlyList<Violation> Violations, string? ErrorReason = null);

/// <summary>Ngưỡng của Oracle. Bất biến theo version; khớp cột của asx.oracle_configs.</summary>
public sealed record OracleConfig(
    int Version,
    double SpeedingToleranceKmh = 5,
    double SpeedingMinDurationS = 2,
    double FreezingMaxStillS = 10,
    double TtcThresholdS = 2,
    double RouteGoalToleranceM = 5)
{
    public static readonly OracleConfig V1 = new(1);
}
