using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoScenarioX.Api.Tests.Data;

/// <summary>Runs in CI against PostgreSQL 18; never touches the application's real database.</summary>
public sealed class PostgreSqlReview2Tests
{
    private static string ConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("ASX_TEST_DATABASE")!;
        var options = new NpgsqlConnectionStringBuilder(value);
        if (!(options.Database ?? "").StartsWith("asx_ci_", StringComparison.Ordinal))
            throw new InvalidOperationException("Integration tests require an isolated database name starting with asx_ci_.");
        return value;
    }

    private static AsxDbContext Db() => new(new DbContextOptionsBuilder<AsxDbContext>()
        .UseNpgsql(ConnectionString()).UseSnakeCaseNamingConvention().Options);

    [PostgreSqlFact]
    public async Task Settings_and_comparison_survive_a_new_connection()
    {
        var comparison = new SimilarityAnalysis { Id = Guid.NewGuid(), Method = "gower", ResultJson = "{\"fixture\":true}", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = Db())
        {
            var repo = new AdministrationRepository(db);
            var setting = await repo.SettingsAsync();
            setting.RetentionDays = 123;
            await repo.SaveSettingsAsync(setting);
            await repo.SaveSimilarityAsync(comparison);
        }
        await using (var reopened = Db())
        {
            var repo = new AdministrationRepository(reopened);
            Assert.Equal(123, (await repo.SettingsAsync()).RetentionDays);
            Assert.Contains("fixture", (await repo.SimilarityAsync(comparison.Id))!.ResultJson);
            reopened.Remove((await reopened.Set<SimilarityAnalysis>().FindAsync(comparison.Id))!);
            await reopened.SaveChangesAsync();
        }
    }

    [PostgreSqlFact]
    public async Task Two_independent_connections_cannot_claim_the_same_run()
    {
        var fixture = await SeedCampaign();
        try
        {
            await using var one = new NpgsqlConnection(ConnectionString());
            await using var two = new NpgsqlConnection(ConnectionString());
            await one.OpenAsync(); await two.OpenAsync();
            await using var held = await one.BeginTransactionAsync();
            await using var commandOne = new NpgsqlCommand("SELECT asx.claim_next_run(@campaign,NULL)", one, held);
            commandOne.Parameters.AddWithValue("campaign", fixture.CampaignId);
            var first = await commandOne.ExecuteScalarAsync();
            Assert.Equal(fixture.RunId, first);
            await using var commandTwo = new NpgsqlCommand("SELECT asx.claim_next_run(@campaign,NULL)", two);
            commandTwo.Parameters.AddWithValue("campaign", fixture.CampaignId);
            commandTwo.CommandTimeout = 5;
            Assert.Equal(DBNull.Value, await commandTwo.ExecuteScalarAsync());
            await held.CommitAsync();
            await using var read = new NpgsqlCommand("SELECT status FROM asx.simulation_runs WHERE id=@id", two);
            read.Parameters.AddWithValue("id", fixture.RunId);
            Assert.Equal("Claimed", await read.ExecuteScalarAsync());
        }
        finally { await DeleteCampaign(fixture.CampaignId); }
    }

    [PostgreSqlFact]
    public async Task Retrying_retains_previous_telemetry_evaluation_and_pinned_config()
    {
        var fixture = await SeedCampaign();
        try
        {
            await using (var db = Db())
            {
                var run = await db.SimulationRuns.Include(x => x.Campaign).SingleAsync(x => x.Id == fixture.RunId);
                run.Status = "Completed"; run.Verdict = "PASS";
                db.RunTelemetry.Add(new() { RunId = run.Id, TelemetryPath = "fixture.jsonl.gz", VideoPath = "fixture.mp4", Outcome = "COMPLETED", DurationS = 12, ReceivedAt = DateTimeOffset.UtcNow });
                db.Evaluations.Add(new() { Id = Guid.NewGuid(), RunId = run.Id, OracleConfigId = run.Campaign.OracleConfigId, Verdict = "PASS", EvaluatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            await using (var db = Db())
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var repo = new RunRepository(db);
                var previous = (await repo.GetForUpdateAsync(fixture.RunId))!;
                var next = await repo.AddRetryAsync(previous);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                Assert.NotEqual(previous.Id, next.Id);
                Assert.Equal(previous.Id, next.PreviousRunId);
                Assert.Equal(2, next.Attempt);
            }
            await using (var reopened = Db())
            {
                Assert.Equal(2, await reopened.SimulationRuns.CountAsync(x => x.CampaignId == fixture.CampaignId));
                Assert.Equal(1, await reopened.SimulationRuns.CountAsync(x => x.CampaignId == fixture.CampaignId && x.IsCurrent));
                Assert.True(await reopened.RunTelemetry.AnyAsync(x => x.RunId == fixture.RunId && x.VideoPath == "fixture.mp4"));
                Assert.True(await reopened.Evaluations.AnyAsync(x => x.RunId == fixture.RunId && x.Verdict == "PASS"));
            }
        }
        finally { await DeleteCampaign(fixture.CampaignId); }
    }

    private static async Task<(Guid CampaignId, Guid RunId)> SeedCampaign()
    {
        await using var db = Db();
        var version = await db.ScenarioVersions.FirstAsync();
        var user = await db.AppUsers.FirstAsync();
        var oracle = await db.OracleConfigs.FirstAsync();
        var campaign = new Campaign { Id = Guid.NewGuid(), Name = "CI isolated campaign", CreatedBy = user.Id, OracleConfigId = oracle.Id };
        var run = new SimulationRun { Id = Guid.NewGuid(), CampaignId = campaign.Id, ScenarioVersionId = version.Id, ContentHash = version.ContentHash, SortOrder = 0 };
        db.Campaigns.Add(campaign); db.SimulationRuns.Add(run);
        await db.SaveChangesAsync();
        return (campaign.Id, run.Id);
    }

    private static async Task DeleteCampaign(Guid id)
    {
        await using var db = Db();
        await db.Campaigns.Where(x => x.Id == id).ExecuteDeleteAsync();
    }
}
