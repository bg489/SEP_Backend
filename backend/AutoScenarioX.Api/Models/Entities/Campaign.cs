namespace AutoScenarioX.Api.Models.Entities;

/// <summary>
/// asx.campaigns — Flow 2 owns create/start/SUT; Flow 3 pins OracleConfigId (ADR 0001).
/// </summary>
public class Campaign
{
    public Guid Id { get; set; }
    public long SeqNo { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Strategy { get; set; } = "proposed_failure_history";
    public string Status { get; set; } = CampaignStatuses.Queued;
    public int Progress { get; set; }
    public decimal PassRate { get; set; }
    public int FailedRuns { get; set; }
    public string EstimatedDuration { get; set; } = "—";

    /// <summary>behavior_agent | autopilot — thuộc campaign, không thuộc scenario.</summary>
    public string SutType { get; set; } = SutTypes.BehaviorAgent;
    /// <summary>cautious | normal | aggressive (chỉ meaningful với behavior_agent).</summary>
    public string SutBehavior { get; set; } = SutBehaviors.Normal;
    public string SutCarlaVersion { get; set; } = "0.9.16";
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Oracle Config version ghim cho mọi Evaluation của campaign (migration 108).</summary>
    public Guid OracleConfigId { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public AppUser? Creator { get; set; }
    public OracleConfig OracleConfig { get; set; } = default!;
    public ICollection<CampaignVersion> Versions { get; set; } = new List<CampaignVersion>();
    public ICollection<SimulationRun> Runs { get; set; } = new List<SimulationRun>();
}

public class CampaignVersion
{
    public Guid CampaignId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public int SortOrder { get; set; }

    public Campaign Campaign { get; set; } = default!;
    public ScenarioVersion ScenarioVersion { get; set; } = default!;
}

public static class CampaignStatuses
{
    public const string Draft = "Draft";
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string NeedsReview = "Needs review";
}

public static class SutTypes
{
    public const string BehaviorAgent = "behavior_agent";
    public const string Autopilot = "autopilot";
}

public static class SutBehaviors
{
    public const string Cautious = "cautious";
    public const string Normal = "normal";
    public const string Aggressive = "aggressive";
}
