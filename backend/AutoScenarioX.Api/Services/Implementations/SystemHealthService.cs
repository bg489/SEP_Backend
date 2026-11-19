using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class SystemHealthService(IUnitOfWork uow) : ISystemHealthService
{
    /// <summary>
    /// All application tables created by the SQL migrations. Compare names rather than counts:
    /// legacy tables or unrelated additions cannot compensate for a missing application table.
    /// Repository views are not base tables and are checked separately from this inventory.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredTables =
    [
        "app_users", "audit_logs", "campaign_versions", "campaigns", "evaluations",
        "failure_analyses", "failure_fault_runs", "failure_faults", "import_records", "map_metadata",
        "oracle_configs", "run_telemetry", "scenario_drafts", "scenario_tags", "scenario_versions",
        "scenarios", "similarity_analyses", "simulation_runs", "simulation_workers", "system_settings",
        "tags", "validation_reports", "variant_batches", "violations"
    ];

    public async Task<HealthResponse> CheckDatabaseAsync(CancellationToken ct = default)
    {
        var status = await uow.GetDatabaseStatusAsync(ct);
        var missing = RequiredTables.Except(status.AsxTables).ToList();
        return new HealthResponse(missing.Count == 0 ? "OK" : "SCHEMA_INCOMPLETE", missing, status.ServerVersion);
    }
}
