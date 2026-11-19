namespace AutoScenarioX.Api.DTOs.Auth;

/// <summary>Hồ sơ người dùng hiện tại (lấy từ asx.app_users).</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive);

/// <summary>Token do Supabase Auth phát hành. Gửi kèm mọi request: Authorization: Bearer &lt;accessToken&gt;.</summary>
public sealed record AuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string RefreshToken,
    CurrentUserResponse User);

public sealed record RegistrationResponse(
    CurrentUserResponse User,
    AuthTokenResponse? Session,
    bool RequiresEmailConfirmation,
    string Message);

public sealed record AuthMessageResponse(string Message);
