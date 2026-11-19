using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Data;

/// <summary>
/// DbContext của hệ thống. Chỉ được dùng bên trong thư mục Repositories.
/// Schema DB được quản lý bằng file SQL trong database/migrations (KHÔNG dùng EF Migrations).
/// </summary>
public class AsxDbContext(DbContextOptions<AsxDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<SimulationWorker> SimulationWorkers => Set<SimulationWorker>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<SimilarityAnalysis> SimilarityAnalyses => Set<SimilarityAnalysis>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<ScenarioDraft> ScenarioDrafts => Set<ScenarioDraft>();
    public DbSet<ScenarioVersion> ScenarioVersions => Set<ScenarioVersion>();
    public DbSet<ValidationReport> ValidationReports => Set<ValidationReport>();
    public DbSet<MapMetadata> MapMetadata => Set<MapMetadata>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ScenarioTag> ScenarioTags => Set<ScenarioTag>();
    public DbSet<ImportRecord> ImportRecords => Set<ImportRecord>();
    public DbSet<VariantBatch> VariantBatches => Set<VariantBatch>();
    public DbSet<OracleConfig> OracleConfigs => Set<OracleConfig>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignVersion> CampaignVersions => Set<CampaignVersion>();
    public DbSet<SimulationRun> SimulationRuns => Set<SimulationRun>();
    public DbSet<RunTelemetry> RunTelemetry => Set<RunTelemetry>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<ViolationEntity> Violations => Set<ViolationEntity>();
    public DbSet<AnalysisSession> FailureAnalyses => Set<AnalysisSession>();
    public DbSet<AnalysisFault> FailureFaults => Set<AnalysisFault>();
    public DbSet<AnalysisFaultRun> FailureFaultRuns => Set<AnalysisFaultRun>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ScenarioRepositoryItem> ScenarioRepositoryView => Set<ScenarioRepositoryItem>();
    public DbSet<ReadyScenarioVersionItem> ReadyScenarioVersionView => Set<ReadyScenarioVersionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("asx");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AsxDbContext).Assembly);
    }
}
