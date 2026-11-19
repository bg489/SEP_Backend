using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

public sealed class AdminOracleServiceTests
{
    private readonly IAdministrationRepository _repo = Substitute.For<IAdministrationRepository>();
    private readonly IOracleConfigRepository _configs = Substitute.For<IOracleConfigRepository>();

    private AdminOracleService Service(string role = AppRoles.Admin)
    {
        var user = FakeUnitOfWork.User(); user.Role.Returns(role);
        _repo.SettingsAsync(Arg.Any<CancellationToken>()).Returns(new SystemSettings());
        _repo.SaveSettingsAsync(Arg.Any<SystemSettings>(), Arg.Any<CancellationToken>()).Returns(x => x.Arg<SystemSettings>());
        return new(_configs, _repo, user);
    }

    [Theory]
    [InlineData(AppRoles.TestEngineer)]
    [InlineData(AppRoles.ScenarioEngineer)]
    [InlineData(AppRoles.TestReviewer)]
    public async Task Non_admin_cannot_change_settings_even_by_direct_service_call(string role)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(role).UpdateSettingsAsync(new(85, 98, 90, false)));
        await _repo.DidNotReceive().SaveSettingsAsync(Arg.Any<SystemSettings>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(90, 80, 90)]
    [InlineData(-1, 90, 90)]
    [InlineData(85, 101, 90)]
    [InlineData(85, 98, 0)]
    public async Task Invalid_thresholds_and_retention_do_not_write(int warning, int block, int days)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => Service().UpdateSettingsAsync(new(warning, block, days, true)));
        await _repo.DidNotReceive().SaveSettingsAsync(Arg.Any<SystemSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settings_are_persisted_without_deleting_any_evidence()
    {
        var response = await Service().UpdateSettingsAsync(new(80, 99, 120, true));
        Assert.Equal(120, response.RetentionDays);
        Assert.Contains("no automatic artifact deletion", response.RetentionNote);
        await _repo.Received(1).SaveSettingsAsync(Arg.Is<SystemSettings>(x => x.RetentionDays == 120 && x.RetentionEnabled), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 10, 2, 5)]
    [InlineData(2, -1, 2, 5)]
    [InlineData(2, 10, 0, 5)]
    [InlineData(2, 10, 2, 0)]
    public async Task New_oracle_versions_validate_thresholds(decimal duration, decimal freeze, decimal ttc, decimal goal)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => Service().CreateAsync(new("new", null, 5, duration, freeze, ttc, goal)));
        await _repo.DidNotReceive().CreateOracleConfigAsync(Arg.Any<OracleConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_version_adds_new_config_and_never_updates_old_one()
    {
        _repo.CreateOracleConfigAsync(Arg.Any<OracleConfig>(), Arg.Any<CancellationToken>()).Returns(x => { var c = x.Arg<OracleConfig>(); c.Version = 2; return c; });
        var response = await Service().CreateAsync(new("safer", "fixture"));
        Assert.Equal(2, response.Version);
        Assert.False(response.IsActive);
        Assert.NotEqual(Guid.Empty, response.Id);
        await _repo.Received(1).CreateOracleConfigAsync(Arg.Any<OracleConfig>(), Arg.Any<CancellationToken>());
    }
}
