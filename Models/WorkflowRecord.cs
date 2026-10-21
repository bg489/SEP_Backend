namespace AutoScenarioX.AuthApi.Models;

public abstract class WorkflowRecord
{
    public string Key { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ScenarioRecord : WorkflowRecord { }
public sealed class ScenarioVersionRecord : WorkflowRecord { }
public sealed class ScenarioArtifactRecord : WorkflowRecord { }
public sealed class CampaignRecord : WorkflowRecord { }
public sealed class CampaignRankingRecord : WorkflowRecord { }
public sealed class ScenarioRunRecord : WorkflowRecord { }
public sealed class RunArtifactRecord : WorkflowRecord { }
public sealed class FailureClusterRecord : WorkflowRecord { }
public sealed class AnalyticsConfigRecord : WorkflowRecord { }
public sealed class AuditLogRecord : WorkflowRecord { }
public sealed class CarlaIntegrationRecord : WorkflowRecord { }
public sealed class SimulationNodeRecord : WorkflowRecord { }
public sealed class OracleRuleRecord : WorkflowRecord { }
public sealed class OracleProfileRecord : WorkflowRecord { }
