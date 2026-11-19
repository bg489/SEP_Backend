using AutoScenarioX.Api.DTOs.Auth;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IAuthService
{
    Task<RegistrationResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthMessageResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<AuthMessageResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<CurrentUserResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default);
    Task<AuthMessageResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
    /// <summary>Đăng nhập bằng email + mật khẩu qua Supabase Auth, trả access token và refresh token.</summary>
    Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Lấy access token mới từ refresh token.</summary>
    Task<AuthTokenResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default);

    /// <summary>Hồ sơ của người đang gọi API.</summary>
    Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken ct = default);
}
