using System.ComponentModel.DataAnnotations;
using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Models;

namespace AutoScenarioX.AuthApi.Contracts;

public sealed class RegisterRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(64)]
    public string Role { get; init; } = RoleNames.ScenarioEngineer;
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Token { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class ChangeRoleRequest
{
    [Required]
    public string Role { get; init; } = string.Empty;
}

public sealed record AuthUserResponse(Guid Id, string FullName, string Email, string Role)
{
    public static AuthUserResponse From(ApplicationUser user, string role) =>
        new(user.Id, user.FullName, user.Email ?? string.Empty, role);
}

public sealed record AuthSessionResponse(string AccessToken, DateTimeOffset ExpiresAt, AuthUserResponse User);

public sealed record MessageResponse(string Message);
