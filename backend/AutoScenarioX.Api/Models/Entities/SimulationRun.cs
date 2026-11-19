namespace AutoScenarioX.Api.Models.Entities;

/// <summary>
/// asx.simulation_runs — Flow 2 queue/status; Flow 3 writes Verdict copy + Telemetry/Evaluations.
/// </summary>
public class SimulationRun
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public int SortOrder { get; set; }
    public string Status { get; set; } = SimulationRunStatuses.Queued;
    public string? Verdict { get; set; }
    public string? ContentHash { get; set; }
    public int? Seed { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Số lần đã re-queue vì lỗi hạ tầng (INFRA_FAILED).</summary>
    public int InfraRetryCount { get; set; }
    public int Attempt { get; set; } = 1;
    public Guid? PreviousRunId { get; set; }
    public bool IsCurrent { get; set; } = true;
    public Guid? WorkerId { get; set; }
    public string? TelemetrySha256 { get; set; }
    public string? ProgressJson { get; set; }
    public DateTimeOffset? ProgressAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Campaign Campaign { get; set; } = default!;
    public ScenarioVersion ScenarioVersion { get; set; } = default!;
    public RunTelemetry? Telemetry { get; set; }
    public ICollection<Evaluation> Evaluations { get; set; } = new List<Evaluation>();
}

/// <summary>Giá trị simulation_runs.status do Flow 2 / worker quản lý.</summary>
public static class SimulationRunStatuses
{
    public const string Queued = "Queued";
    public const string Claimed = "Claimed";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
    public const string Error = "Error";
}

/// <summary>asx.run_telemetry — Telemetry đã nhận của một simulation run (Flow 3).</summary>
public class RunTelemetry
{
    public Guid RunId { get; set; }
    public string TelemetryPath { get; set; } = default!;
    public string? VideoPath { get; set; }
    public string? Outcome { get; set; }
    public decimal? DurationS { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? VideoSha256 { get; set; }
}

/// <summary>Run Outcome do runner ghi trong bản ghi end của Telemetry (không phải Verdict).</summary>
public static class RunOutcomes
{
    public const string Completed = "COMPLETED";
    public const string TimedOut = "TIMED_OUT";
    public const string StoppedOnViolation = "STOPPED_ON_VIOLATION";
    public const string InfraFailed = "INFRA_FAILED";
    /// <summary>Worker cắt sớm (QUICK). Không phải hết timeout của scenario.</summary>
    public const string Truncated = "TRUNCATED";

    public static readonly string[] All = [Completed, TimedOut, StoppedOnViolation, InfraFailed, Truncated];
}
