using System.Text.Json;
using AutoScenarioX.AuthApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.AuthApi.Data;

/// <summary>
/// Persists each workflow area in its own PostgreSQL table while keeping the API's
/// existing JSON contracts stable during frontend/backend integration.
/// </summary>
public sealed class WorkflowDocumentRepository(AuthDbContext database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<T?> GetAsync<T>(string area, string key, CancellationToken cancellationToken) => area switch
    {
        "scenario" => GetAsync<ScenarioRecord, T>(database.Scenarios, key, cancellationToken),
        "scenario-versions" => GetAsync<ScenarioVersionRecord, T>(database.ScenarioVersions, key, cancellationToken),
        "scenario-artifact" => GetAsync<ScenarioArtifactRecord, T>(database.ScenarioArtifacts, key, cancellationToken),
        "campaign" => GetAsync<CampaignRecord, T>(database.Campaigns, key, cancellationToken),
        "campaign-ranking" => GetAsync<CampaignRankingRecord, T>(database.CampaignRankings, key, cancellationToken),
        "run" => GetAsync<ScenarioRunRecord, T>(database.ScenarioRuns, key, cancellationToken),
        "run-artifact" => GetAsync<RunArtifactRecord, T>(database.RunArtifacts, key, cancellationToken),
        "failure" => GetAsync<FailureClusterRecord, T>(database.FailureClusters, key, cancellationToken),
        "analytics" => GetAsync<AnalyticsConfigRecord, T>(database.AnalyticsConfigs, key, cancellationToken),
        "audit" => GetAsync<AuditLogRecord, T>(database.AuditLogs, key, cancellationToken),
        "integration" => GetAsync<CarlaIntegrationRecord, T>(database.CarlaIntegrations, key, cancellationToken),
        "worker" => GetAsync<SimulationNodeRecord, T>(database.SimulationNodes, key, cancellationToken),
        "oracle-rule" => GetAsync<OracleRuleRecord, T>(database.OracleRules, key, cancellationToken),
        "oracle-profile" => GetAsync<OracleProfileRecord, T>(database.OracleProfiles, key, cancellationToken),
        _ => throw UnknownArea(area)
    };

    public Task<List<T>> ListAsync<T>(string area, CancellationToken cancellationToken) => area switch
    {
        "scenario" => ListAsync<ScenarioRecord, T>(database.Scenarios, cancellationToken),
        "scenario-versions" => ListAsync<ScenarioVersionRecord, T>(database.ScenarioVersions, cancellationToken),
        "scenario-artifact" => ListAsync<ScenarioArtifactRecord, T>(database.ScenarioArtifacts, cancellationToken),
        "campaign" => ListAsync<CampaignRecord, T>(database.Campaigns, cancellationToken),
        "campaign-ranking" => ListAsync<CampaignRankingRecord, T>(database.CampaignRankings, cancellationToken),
        "run" => ListAsync<ScenarioRunRecord, T>(database.ScenarioRuns, cancellationToken),
        "run-artifact" => ListAsync<RunArtifactRecord, T>(database.RunArtifacts, cancellationToken),
        "failure" => ListAsync<FailureClusterRecord, T>(database.FailureClusters, cancellationToken),
        "analytics" => ListAsync<AnalyticsConfigRecord, T>(database.AnalyticsConfigs, cancellationToken),
        "audit" => ListAsync<AuditLogRecord, T>(database.AuditLogs, cancellationToken),
        "integration" => ListAsync<CarlaIntegrationRecord, T>(database.CarlaIntegrations, cancellationToken),
        "worker" => ListAsync<SimulationNodeRecord, T>(database.SimulationNodes, cancellationToken),
        "oracle-rule" => ListAsync<OracleRuleRecord, T>(database.OracleRules, cancellationToken),
        "oracle-profile" => ListAsync<OracleProfileRecord, T>(database.OracleProfiles, cancellationToken),
        _ => throw UnknownArea(area)
    };

    public Task SaveAsync<T>(string area, string key, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        return area switch
        {
            "scenario" => SaveAsync(database.Scenarios, key, payload, () => new ScenarioRecord(), cancellationToken),
            "scenario-versions" => SaveAsync(database.ScenarioVersions, key, payload, () => new ScenarioVersionRecord(), cancellationToken),
            "scenario-artifact" => SaveAsync(database.ScenarioArtifacts, key, payload, () => new ScenarioArtifactRecord(), cancellationToken),
            "campaign" => SaveAsync(database.Campaigns, key, payload, () => new CampaignRecord(), cancellationToken),
            "campaign-ranking" => SaveAsync(database.CampaignRankings, key, payload, () => new CampaignRankingRecord(), cancellationToken),
            "run" => SaveAsync(database.ScenarioRuns, key, payload, () => new ScenarioRunRecord(), cancellationToken),
            "run-artifact" => SaveAsync(database.RunArtifacts, key, payload, () => new RunArtifactRecord(), cancellationToken),
            "failure" => SaveAsync(database.FailureClusters, key, payload, () => new FailureClusterRecord(), cancellationToken),
            "analytics" => SaveAsync(database.AnalyticsConfigs, key, payload, () => new AnalyticsConfigRecord(), cancellationToken),
            "audit" => SaveAsync(database.AuditLogs, key, payload, () => new AuditLogRecord(), cancellationToken),
            "integration" => SaveAsync(database.CarlaIntegrations, key, payload, () => new CarlaIntegrationRecord(), cancellationToken),
            "worker" => SaveAsync(database.SimulationNodes, key, payload, () => new SimulationNodeRecord(), cancellationToken),
            "oracle-rule" => SaveAsync(database.OracleRules, key, payload, () => new OracleRuleRecord(), cancellationToken),
            "oracle-profile" => SaveAsync(database.OracleProfiles, key, payload, () => new OracleProfileRecord(), cancellationToken),
            _ => throw UnknownArea(area)
        };
    }

    public Task ReplaceAsync<T>(string area, IEnumerable<T> values, Func<T, string> keySelector, CancellationToken cancellationToken) => area switch
    {
        "scenario" => ReplaceAsync(database.Scenarios, values, keySelector, () => new ScenarioRecord(), cancellationToken),
        "scenario-versions" => ReplaceAsync(database.ScenarioVersions, values, keySelector, () => new ScenarioVersionRecord(), cancellationToken),
        "scenario-artifact" => ReplaceAsync(database.ScenarioArtifacts, values, keySelector, () => new ScenarioArtifactRecord(), cancellationToken),
        "campaign" => ReplaceAsync(database.Campaigns, values, keySelector, () => new CampaignRecord(), cancellationToken),
        "campaign-ranking" => ReplaceAsync(database.CampaignRankings, values, keySelector, () => new CampaignRankingRecord(), cancellationToken),
        "run" => ReplaceAsync(database.ScenarioRuns, values, keySelector, () => new ScenarioRunRecord(), cancellationToken),
        "run-artifact" => ReplaceAsync(database.RunArtifacts, values, keySelector, () => new RunArtifactRecord(), cancellationToken),
        "failure" => ReplaceAsync(database.FailureClusters, values, keySelector, () => new FailureClusterRecord(), cancellationToken),
        "analytics" => ReplaceAsync(database.AnalyticsConfigs, values, keySelector, () => new AnalyticsConfigRecord(), cancellationToken),
        "audit" => ReplaceAsync(database.AuditLogs, values, keySelector, () => new AuditLogRecord(), cancellationToken),
        "integration" => ReplaceAsync(database.CarlaIntegrations, values, keySelector, () => new CarlaIntegrationRecord(), cancellationToken),
        "worker" => ReplaceAsync(database.SimulationNodes, values, keySelector, () => new SimulationNodeRecord(), cancellationToken),
        "oracle-rule" => ReplaceAsync(database.OracleRules, values, keySelector, () => new OracleRuleRecord(), cancellationToken),
        "oracle-profile" => ReplaceAsync(database.OracleProfiles, values, keySelector, () => new OracleProfileRecord(), cancellationToken),
        _ => throw UnknownArea(area)
    };

    private static async Task<T?> GetAsync<TRecord, T>(DbSet<TRecord> table, string key, CancellationToken cancellationToken)
        where TRecord : WorkflowRecord
    {
        var record = await table.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        return record is null ? default : JsonSerializer.Deserialize<T>(record.Payload, JsonOptions);
    }

    private static async Task<List<T>> ListAsync<TRecord, T>(DbSet<TRecord> table, CancellationToken cancellationToken)
        where TRecord : WorkflowRecord
    {
        var records = await table.AsNoTracking().OrderByDescending(item => item.UpdatedAt).ToListAsync(cancellationToken);
        return records.Select(item => JsonSerializer.Deserialize<T>(item.Payload, JsonOptions)!)
            .Where(item => item is not null)
            .ToList();
    }

    private async Task SaveAsync<TRecord>(DbSet<TRecord> table, string key, string payload, Func<TRecord> factory, CancellationToken cancellationToken)
        where TRecord : WorkflowRecord
    {
        // Workflow records share an EF TPC hierarchy for column reuse, but their
        // domain keys are scoped to separate tables (for example, a scenario and
        // its version intentionally use the same key). Keep writes isolated from
        // other tracked workflow rows so EF does not treat those keys as duplicates.
        database.ChangeTracker.Clear();
        var record = await table.SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (record is null)
        {
            record = factory();
            record.Key = key;
            table.Add(record);
        }
        record.Payload = payload;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private async Task ReplaceAsync<TRecord, TValue>(DbSet<TRecord> table, IEnumerable<TValue> values, Func<TValue, string> keySelector, Func<TRecord> factory, CancellationToken cancellationToken)
        where TRecord : WorkflowRecord
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            table.RemoveRange(await table.ToListAsync(cancellationToken));
            await database.SaveChangesAsync(cancellationToken);
            database.ChangeTracker.Clear();

            var updatedAt = DateTimeOffset.UtcNow;
            foreach (var value in values)
            {
                var record = factory();
                record.Key = keySelector(value);
                record.Payload = JsonSerializer.Serialize(value, JsonOptions);
                record.UpdatedAt = updatedAt;
                table.Add(record);
            }
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private static ArgumentOutOfRangeException UnknownArea(string area) =>
        new(nameof(area), area, "Unknown workflow persistence area.");
}
