using System.Security.Claims;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;

namespace AutoScenarioX.Api.Common.Auth;

/// <summary>
/// Người dùng hiện tại đọc từ claims của request.
/// Dùng chung cho CẢ hai chế độ: Dev (claims do DevAuthenticationHandler sinh)
/// và Supabase (claims từ JWT + role lấy từ asx.app_users lúc xác thực).
/// Service KHÔNG đọc HttpContext trực tiếp, chỉ dùng ICurrentUser.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid UserId
    {
        get
        {
            var subject = Principal?.FindFirstValue(AsxClaimTypes.Subject)
                          ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(subject, out var id)
                ? id
                : throw new UnauthorizedException(ErrorCodes.Unauthorized,
                    "Unable to identify the user. Sign in and include a Bearer token.");
        }
    }

    public string? Email => Principal?.FindFirstValue(AsxClaimTypes.Email);

    public string Role => Principal?.FindFirstValue(AsxClaimTypes.AppRole) ?? AppRoles.ScenarioEngineer;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}
