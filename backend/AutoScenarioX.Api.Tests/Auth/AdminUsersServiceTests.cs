using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Auth;

public class AdminUsersServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ICurrentUser _admin = FakeUnitOfWork.User();
    private readonly AdminUsersService _service;
    private readonly AppUser _target = new() { Id = Guid.NewGuid(), Email = "target@example.com", DisplayName = "Target", Role = AppRoles.TestEngineer, IsActive = true };

    public AdminUsersServiceTests()
    {
        _admin.Role.Returns(AppRoles.Admin);
        _service = new AdminUsersService(_uow, _admin);
        _uow.AppUsers.FindAsync(Arg.Is<object[]>(keys => (Guid)keys[0] == _target.Id), Arg.Any<CancellationToken>()).Returns(_target);
    }

    [Theory]
    [InlineData(AppRoles.ScenarioEngineer)]
    [InlineData(AppRoles.TestEngineer)]
    [InlineData(AppRoles.TestReviewer)]
    public async Task Non_admin_cannot_list_or_modify_accounts(string role)
    {
        _admin.Role.Returns(role);
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(null, null, null, 1, 25));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.UpdateAsync(_target.Id, new UpdateUserRequest { Role = AppRoles.Admin }));
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Admin_changes_role_and_active_status_and_writes_audit()
    {
        var result = await _service.UpdateAsync(_target.Id, new UpdateUserRequest { Role = AppRoles.TestReviewer, IsActive = false });
        Assert.Equal(AppRoles.TestReviewer, result.Role);
        Assert.False(result.IsActive);
        Assert.Equal(1, _uow.SaveCount);
        _uow.AuditLogs.Received(1).Write("USER", _target.Id, "ADMIN_UPDATE_USER", _admin.UserId, Arg.Any<object?>());
    }

    [Fact]
    public async Task Unknown_roles_are_rejected_before_database_change()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => _service.UpdateAsync(_target.Id, new UpdateUserRequest { Role = "OWNER" }));
        Assert.Equal(AppRoles.TestEngineer, _target.Role);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Administrator_cannot_disable_or_demote_self()
    {
        _admin.UserId.Returns(_target.Id);
        _target.Role = AppRoles.Admin;
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateAsync(_target.Id, new UpdateUserRequest { IsActive = false }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateAsync(_target.Id, new UpdateUserRequest { Role = AppRoles.TestEngineer }));
        Assert.True(_target.IsActive);
        Assert.Equal(AppRoles.Admin, _target.Role);
    }

    [Fact]
    public async Task Last_active_administrator_cannot_be_disabled_or_demoted()
    {
        _target.Role = AppRoles.Admin;
        _uow.AppUsers.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateAsync(_target.Id, new UpdateUserRequest { Role = AppRoles.TestEngineer }));
        Assert.Equal("LAST_ADMIN_REQUIRED", error.Code);
        Assert.Equal(0, _uow.SaveCount);
        Received.InOrder(() =>
        {
            _uow.AppUsers.LockAdministrationAsync(Arg.Any<CancellationToken>());
            _uow.AppUsers.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>());
            _uow.AppUsers.CountActiveAdminsAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task One_of_multiple_active_administrators_can_be_disabled()
    {
        _target.Role = AppRoles.Admin;
        _uow.AppUsers.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(2);
        var result = await _service.UpdateAsync(_target.Id, new UpdateUserRequest { IsActive = false });
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Search_returns_pagination_and_filters()
    {
        _uow.AppUsers.SearchAsync("target", AppRoles.TestEngineer, true, 2, 10, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<AppUser>)[_target], 11));
        var result = await _service.SearchAsync("target", "test_engineer", true, 2, 10);
        Assert.Equal(11, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(_target.Id, Assert.Single(result.Items).Id);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_page_parameters_are_rejected(int page, int pageSize)
        => await Assert.ThrowsAsync<RequestValidationException>(() => _service.SearchAsync(null, null, null, page, pageSize));
}

public class UserProvisioningTests
{
    [Fact]
    public async Task Disabled_account_is_denied_even_with_valid_external_identity()
    {
        var uow = new FakeUnitOfWork();
        var id = Guid.NewGuid();
        uow.AppUsers.GetReadOnlyAsync(id, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = id, IsActive = false });
        var service = new UserProvisioningService(uow, Options.Create(new AuthOptions()));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.EnsureAsync(id, "u@example.com"));
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Automatic_provisioning_never_grants_admin_even_if_default_is_misconfigured()
    {
        var uow = new FakeUnitOfWork();
        var service = new UserProvisioningService(uow, Options.Create(new AuthOptions { DefaultRole = AppRoles.Admin }));
        var result = await service.EnsureAsync(Guid.NewGuid(), "u@example.com");
        Assert.Equal(AppRoles.ScenarioEngineer, result.Role);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task Existing_role_is_preserved_by_provisioning()
    {
        var uow = new FakeUnitOfWork();
        var id = Guid.NewGuid();
        uow.AppUsers.GetReadOnlyAsync(id, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = id, IsActive = true, Role = AppRoles.TestReviewer });
        var service = new UserProvisioningService(uow, Options.Create(new AuthOptions { DefaultRole = AppRoles.Admin }));
        Assert.Equal(AppRoles.TestReviewer, (await service.EnsureAsync(id, "u@example.com")).Role);
        Assert.Equal(0, uow.SaveCount);
    }
}
