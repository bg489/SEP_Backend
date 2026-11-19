using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Services;

public class SystemHealthServiceTests
{
    private readonly FakeUnitOfWork _uow = new();

    [Fact]
    public async Task Tables_of_other_flows_do_not_make_the_schema_incomplete()
    {
        _uow.AsxTables = [.. SystemHealthService.RequiredTables, "campaigns", "campaign_versions"];

        var health = await new SystemHealthService(_uow).CheckDatabaseAsync();

        Assert.Equal("OK", health.Status);
        Assert.Empty(health.MissingTables);
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
