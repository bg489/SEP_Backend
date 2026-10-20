using AutoScenarioX.AuthApi.Models;

namespace AutoScenarioX.AuthApi.Auth;

public interface IJwtTokenService
{
    Task<IssuedAccessToken> IssueAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);
