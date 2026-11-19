using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Auth;

/// <summary>
/// Đăng nhập qua Supabase Auth.
/// Frontend có thể gọi thẳng Supabase JS SDK; endpoint này để thử API trong Swagger
/// và để nơi gọi không cần biết cấu hình Supabase.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RegistrationResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await authService.RegisterAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<ActionResult<AuthMessageResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
        => Ok(await authService.ForgotPasswordAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<ActionResult<AuthMessageResponse>> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
        => Ok(await authService.ResetPasswordAsync(request, ct));

    [Authorize]
    [HttpPatch("profile")]
    public async Task<ActionResult<CurrentUserResponse>> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
        => Ok(await authService.UpdateProfileAsync(request, ct));

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<AuthMessageResponse>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
        => Ok(await authService.ChangePasswordAsync(request, ct));

    /// <summary>Đăng nhập bằng email + mật khẩu. Trả access token dùng cho header Authorization: Bearer.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokenResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await authService.LoginAsync(request, ct));

    /// <summary>Lấy access token mới bằng refresh token.</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokenResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => Ok(await authService.RefreshAsync(request, ct));

    /// <summary>Hồ sơ của người đang gọi API (id, email, role trong asx.app_users).</summary>
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken ct)
        => Ok(await authService.GetCurrentUserAsync(ct));
}
