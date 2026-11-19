using System.Security.Claims;
using System.Text.Encodings.Web;
using AutoScenarioX.Api.Common.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Common.Auth;

/// <summary>
/// CHỈ DÙNG KHI DEV (Auth:Mode = Dev): mọi request được coi như user cố định trong section DevAuth,
/// không cần token. Nhờ vậy [Authorize] và policy phân quyền chạy giống hệt chế độ Supabase,
/// nhóm vẫn làm việc được khi chưa cấu hình Supabase Auth.
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<DevAuthOptions> devAuth) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = devAuth.Value;
        var role = options.Role;
        if (Request.Headers.TryGetValue("X-Asx-Dev-Role", out var requested))
        {
            var picked = requested.ToString();
            if (AppRoles.All.Contains(picked))
                role = picked;
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(AsxClaimTypes.Subject, options.UserId.ToString()),
                new Claim(AsxClaimTypes.Email, "dev.engineer@autoscenariox.local"),
                new Claim(AsxClaimTypes.AppRole, role)
            ],
            SchemeName, AsxClaimTypes.Subject, AsxClaimTypes.AppRole);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
