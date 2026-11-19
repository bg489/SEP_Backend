namespace AutoScenarioX.Api.DTOs.Auth;

/// <summary>Body của POST /api/v1/auth/login.</summary>
public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Body của POST /api/v1/auth/refresh.</summary>
public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "SCENARIO_ENGINEER";
}

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    public string AccessToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class UpdateProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class UpdateUserRequest
{
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
}
