using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Tests.Data;

/// <summary>
/// Model EF dựng được offline và khớp tên bảng/cột thật: bảng Flow 2 (campaigns, simulation_runs, đọc từ DB chung
/// 2026-10-20) và bảng Flow 3 của migration 108. Không cần DB.
/// </summary>
public class EfModelTests
{
    private static AsxDbContext Db() => new(new DbContextOptionsBuilder<AsxDbContext>()
        .UseNpgsql("Host=localhost;Database=x").UseSnakeCaseNamingConvention().Options);

    [Fact]
    public void Health_inventory_covers_every_mapped_application_table_and_excludes_views()
    {
        using var db = Db();
        var tables = db.Model.GetEntityTypes().Select(x => x.GetTableName())
            .Where(x => x is not null).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(tables, SystemHealthService.RequiredTables.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(typeof(Campaign), "campaigns", new[] { "id", "code", "name", "oracle_config_id", "sut_type", "strategy" })]
    [InlineData(typeof(SimulationRun), "simulation_runs", new[]
        { "id", "campaign_id", "scenario_version_id", "sort_order", "status", "verdict", "content_hash", "seed", "started_at", "finished_at", "error_message", "infra_retry_count", "created_at", "updated_at" })]
    [InlineData(typeof(RunTelemetry), "run_telemetry", new[] { "run_id", "telemetry_path", "video_path", "outcome", "duration_s", "received_at" })]
    [InlineData(typeof(OracleConfig), "oracle_configs", new[] { "id", "version", "speeding_tolerance_kmh", "route_goal_tolerance_m" })]
    [InlineData(typeof(Evaluation), "evaluations", new[] { "id", "run_id", "oracle_config_id", "verdict", "error_reason", "evaluated_at" })]
    [InlineData(typeof(ViolationEntity), "violations", new[] { "id", "evaluation_id", "check_name", "severity", "t", "duration_s", "details" })]
    public void Entity_maps_to_existing_table_and_columns(Type type, string table, string[] columns)
    {
        using var db = Db();
        var entity = db.Model.FindEntityType(type)!;

        Assert.Equal(table, entity.GetTableName());
        var mapped = entity.GetProperties().Select(p => p.GetColumnName()).ToHashSet();
        Assert.All(columns, c => Assert.Contains(c, mapped));
    }
}
