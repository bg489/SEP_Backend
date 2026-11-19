using AutoScenarioX.AuthApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.AuthApi.Data;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ScenarioRecord> Scenarios => Set<ScenarioRecord>();
    public DbSet<ScenarioVersionRecord> ScenarioVersions => Set<ScenarioVersionRecord>();
    public DbSet<ScenarioArtifactRecord> ScenarioArtifacts => Set<ScenarioArtifactRecord>();
    public DbSet<CampaignRecord> Campaigns => Set<CampaignRecord>();
    public DbSet<CampaignRankingRecord> CampaignRankings => Set<CampaignRankingRecord>();
    public DbSet<ScenarioRunRecord> ScenarioRuns => Set<ScenarioRunRecord>();
    public DbSet<RunArtifactRecord> RunArtifacts => Set<RunArtifactRecord>();
    public DbSet<FailureClusterRecord> FailureClusters => Set<FailureClusterRecord>();
    public DbSet<AnalyticsConfigRecord> AnalyticsConfigs => Set<AnalyticsConfigRecord>();
    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();
    public DbSet<CarlaIntegrationRecord> CarlaIntegrations => Set<CarlaIntegrationRecord>();
    public DbSet<SimulationNodeRecord> SimulationNodes => Set<SimulationNodeRecord>();
    public DbSet<OracleRuleRecord> OracleRules => Set<OracleRuleRecord>();
    public DbSet<OracleProfileRecord> OracleProfiles => Set<OracleProfileRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("app_auth");
        builder.Entity<ApplicationUser>().Property(user => user.FullName).HasMaxLength(160).IsRequired();
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<WorkflowRecord>(entity =>
        {
            entity.UseTpcMappingStrategy();
            entity.HasKey(record => record.Key);
            entity.Property(record => record.Key).HasMaxLength(160);
            entity.Property(record => record.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(record => record.UpdatedAt).IsRequired();
        });
        ConfigureWorkflowRecord<ScenarioRecord>(builder, "scenarios");
        ConfigureWorkflowRecord<ScenarioVersionRecord>(builder, "scenario_versions");
        ConfigureWorkflowRecord<ScenarioArtifactRecord>(builder, "scenario_artifacts");
        ConfigureWorkflowRecord<CampaignRecord>(builder, "campaigns");
        ConfigureWorkflowRecord<CampaignRankingRecord>(builder, "campaign_rankings");
        ConfigureWorkflowRecord<ScenarioRunRecord>(builder, "scenario_runs");
        ConfigureWorkflowRecord<RunArtifactRecord>(builder, "run_artifacts");
        ConfigureWorkflowRecord<FailureClusterRecord>(builder, "failure_clusters");
        ConfigureWorkflowRecord<AnalyticsConfigRecord>(builder, "analytics_configs");
        ConfigureWorkflowRecord<AuditLogRecord>(builder, "audit_logs");
        ConfigureWorkflowRecord<CarlaIntegrationRecord>(builder, "carla_integrations");
        ConfigureWorkflowRecord<SimulationNodeRecord>(builder, "simulation_nodes");
        ConfigureWorkflowRecord<OracleRuleRecord>(builder, "oracle_rules");
        ConfigureWorkflowRecord<OracleProfileRecord>(builder, "oracle_profiles");
    }

    private static void ConfigureWorkflowRecord<TRecord>(ModelBuilder builder, string table)
        where TRecord : WorkflowRecord
    {
        builder.Entity<TRecord>(entity =>
        {
            entity.ToTable(table, "app_workflow");
            entity.HasBaseType(typeof(WorkflowRecord));
            entity.HasIndex(record => record.UpdatedAt);
        });
    }
}
