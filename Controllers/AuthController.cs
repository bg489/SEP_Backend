using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenService tokenService,
    IWebHostEnvironment environment,
    ILogger<AuthController> logger) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthSessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthSessionResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var role = request.Role.Trim();
        if (!RoleNames.SelfSelectable.Contains(role, StringComparer.Ordinal))
        {
            return BadRequest(new MessageResponse(
                "Choose Scenario Engineer, Test Engineer, or Test Reviewer. Administrator cannot be selected during registration."));
        }

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim()
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(new MessageResponse(
                string.Join(" ", createResult.Errors.Select(error => error.Description))));
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new MessageResponse("The account could not be assigned its initial role."));
        }

        return StatusCode(StatusCodes.Status201Created,
            await CreateSessionAsync(user, role, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthSessionResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Unauthorized(new MessageResponse("Invalid email or password."));
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return StatusCode(423, new MessageResponse("This account is temporarily locked. Try again later."));
        }
        if (!result.Succeeded)
        {
            return Unauthorized(new MessageResponse("Invalid email or password."));
        }

        var roles = await userManager.GetRolesAsync(user);
        var role = RoleNames.All.FirstOrDefault(roles.Contains);
        if (role is null)
        {
            logger.LogError("User {UserId} has no AutoScenarioX role assigned.", user.Id);
            return StatusCode(StatusCodes.Status403Forbidden,
                new MessageResponse("This account does not have an application role."));
        }

        return Ok(await CreateSessionAsync(user, role, cancellationToken));
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<AuthUserResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthUserResponse>> Me()
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userId, out var id)) return Unauthorized();

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        var role = RoleNames.All.FirstOrDefault(roles.Contains);
        if (role is null) return Forbid();
        return Ok(AuthUserResponse.From(user, role));
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MessageResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        const string response = "If the account exists, password reset instructions will be sent when email delivery is configured.";
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Development-only password reset token for {Email}: {ResetToken}",
                    user.Email,
                    token);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Accepted(new MessageResponse(response));
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MessageResponse>> ResetPassword(ResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null) return BadRequest(new MessageResponse("The reset request is invalid or expired."));

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new MessageResponse(
                string.Join(" ", result.Errors.Select(error => error.Description))));
        }

        return Ok(new MessageResponse("Password updated. You can sign in with the new password."));
    }

    private async Task<AuthSessionResponse> CreateSessionAsync(
        ApplicationUser user,
        string role,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.IssueAsync(user, cancellationToken);
        return new AuthSessionResponse(token.Value, token.ExpiresAt, AuthUserResponse.From(user, role));
    }
}
