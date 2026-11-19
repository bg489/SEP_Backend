using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class UserProvisioningService(IUnitOfWork uow, IOptions<AuthOptions> options) : IUserProvisioningService
{
    private readonly AuthOptions _options = options.Value;

    public async Task<AppUser> EnsureAsync(Guid userId, string? email, CancellationToken ct = default)
    {
        if (await uow.AppUsers.GetReadOnlyAsync(userId, ct) is { } existing)
        {
            if (!existing.IsActive)
                throw new ForbiddenException(ErrorCodes.Forbidden, "This account has been disabled.");
            return existing;
        }

        if (!_options.AutoProvisionUsers)
            throw new ForbiddenException(ErrorCodes.UserNotProvisioned,
                "This account does not have access. Contact an administrator.");

        // An unregistered external account must never become an administrator implicitly.
        var role = _options.DefaultRole != AppRoles.Admin && AppRoles.All.Contains(_options.DefaultRole)
            ? _options.DefaultRole : AppRoles.ScenarioEngineer;
        var address = string.IsNullOrWhiteSpace(email) ? $"{userId}@unknown.local" : email.Trim();

        var user = new AppUser
        {
            Id = userId,
            Email = address,
            DisplayName = address.Split('@')[0],
            Role = role,
            IsActive = true
        };

        uow.AppUsers.Add(user);
        await uow.SaveChangesAsync(ct);
        return user;
    }
}
