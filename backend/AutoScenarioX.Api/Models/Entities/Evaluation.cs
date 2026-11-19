namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.evaluations - một lần chấm một simulation run (Flow 2) với một Oracle Config (BẤT BIẾN).</summary>
public class Evaluation
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid OracleConfigId { get; set; }
    public string Verdict { get; set; } = default!;        // "PASS" | "FAIL" | "ERROR"
    public string? ErrorReason { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }

    // Navigation properties
    public SimulationRun Run { get; set; } = default!;
    public OracleConfig OracleConfig { get; set; } = default!;
    public ICollection<ViolationEntity> Violations { get; set; } = new List<ViolationEntity>();
}

public static class Verdicts
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Error = "ERROR";
}
