using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Services;

public class SystemHealthServiceTests
{
    private readonly FakeUnitOfWork _uow = new();

    [Fact]
    public async Task Extra_legacy_or_unrelated_tables_do_not_make_the_schema_incomplete()
    {
        _uow.AsxTables = [.. SystemHealthService.RequiredTables, "test_campaigns", "unrelated_table"];

        var health = await new SystemHealthService(_uow).CheckDatabaseAsync();

        Assert.Equal("OK", health.Status);
        Assert.Empty(health.MissingTables);
    }

    [Theory]
    [InlineData("campaigns")]
    [InlineData("oracle_configs")]
    [InlineData("run_telemetry")]
    [InlineData("failure_analyses")]
    [InlineData("simulation_workers")]
    [InlineData("system_settings")]
    [InlineData("similarity_analyses")]
    public async Task Missing_execution_analysis_or_admin_table_is_unhealthy(string missing)
    {
        _uow.AsxTables = [.. SystemHealthService.RequiredTables.Where(t => t != missing), "unrelated_table"];

        var health = await new SystemHealthService(_uow).CheckDatabaseAsync();

        Assert.Equal("SCHEMA_INCOMPLETE", health.Status);
        Assert.Equal([missing], health.MissingTables);
    }

    [Fact]
    public async Task Missing_flow1_tables_are_named()
    {
        _uow.AsxTables = SystemHealthService.RequiredTables.Where(t => t is not ("tags" or "scenario_tags")).ToList();

        var health = await new SystemHealthService(_uow).CheckDatabaseAsync();

        Assert.Equal("SCHEMA_INCOMPLETE", health.Status);
        Assert.Equal(["scenario_tags", "tags"], health.MissingTables);
    }
}
