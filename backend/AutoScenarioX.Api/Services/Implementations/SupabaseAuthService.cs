using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

/// <summary>
/// Đăng nhập bằng cách gọi GoTrue (Supabase Auth) thay cho frontend.
/// Có endpoint này để thử API trong Swagger khi frontend React chưa làm;
/// sau này frontend gọi thẳng Supabase JS SDK cũng được, backend vẫn chỉ cần xác thực JWT.
/// </summary>
public sealed class SupabaseAuthService(
    IHttpClientFactory httpClientFactory,
    IUserProvisioningService provisioning,
    ICurrentUser currentUser,
    IOptions<AuthOptions> options,
    IUnitOfWork uow) : IAuthService
{
    public const string HttpClientName = "supabase-auth";

    private readonly AuthOptions _options = options.Value;

    public async Task<RegistrationResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();
        var email = ValidateEmail(request.Email);
        ValidatePassword(request.Password);
        var displayName = ValidateDisplayName(request.DisplayName);
        var role = request.Role?.Trim().ToUpperInvariant() ?? string.Empty;
        if (role == AppRoles.Admin || !AppRoles.All.Contains(role))
            throw Invalid("role", "Choose Scenario Engineer, Test Engineer or Test Reviewer. Administrator cannot be self-registered.");
        if (await uow.AppUsers.EmailExistsAsync(email, ct))
            throw new ConflictException("EMAIL_ALREADY_REGISTERED", "This email is already registered.");

        using var result = await SendAsync(HttpMethod.Post, "signup", new
        {
            email,
            password = request.Password,
            data = new { display_name = displayName }
        }, null, ct);
        var root = result.RootElement;
        var userElement = root.TryGetProperty("user", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : root;
        var userId = ReadUserId(userElement);
        // GoTrue may obscure an existing account by returning a user with no identities.
        if (userElement.TryGetProperty("identities", out var identities) && identities.ValueKind == JsonValueKind.Array && identities.GetArrayLength() == 0)
            throw new ConflictException("EMAIL_ALREADY_REGISTERED", "This email is already registered. Try signing in or resetting your password.");
        var existing = await uow.AppUsers.GetReadOnlyAsync(userId, ct);
        if (existing is not null)
            throw new ConflictException("EMAIL_ALREADY_REGISTERED", "This account already exists.");

        var appUser = new AppUser
        {
            Id = userId, Email = email, DisplayName = displayName, Role = role, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        uow.AppUsers.Add(appUser);
        await uow.SaveChangesAsync(ct);
        var accessToken = GetString(root, "access_token");
        var session = string.IsNullOrWhiteSpace(accessToken) ? null : TokenResponse(root, appUser);
        return new RegistrationResponse(appUser.ToResponse(), session, session is null,
            session is null ? "Account created. Check your email to confirm your account before signing in." : "Account created.");
    }

    public async Task<AuthMessageResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var email = ValidateEmail(request.Email);
        var endpoint = "recover";
        if (!string.IsNullOrWhiteSpace(_options.PasswordResetRedirectUrl))
            endpoint += "?redirect_to=" + Uri.EscapeDataString(_options.PasswordResetRedirectUrl);
        using var result = await SendAsync(HttpMethod.Post, endpoint, new { email }, null, ct);
        return new AuthMessageResponse("If an account exists for this email, a password reset link has been sent. The link expiry is controlled by Supabase Auth.");
    }

    public async Task<AuthMessageResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        ValidatePassword(request.NewPassword);
        if (string.IsNullOrWhiteSpace(request.AccessToken)) throw Invalid("accessToken", "Open a valid recovery link from your email.");
        // Supabase verifies the bearer token server-side; never trust decoded client claims.
        using var identity = await SendAsync(HttpMethod.Get, "user", null, request.AccessToken, ct);
        _ = ReadUserId(identity.RootElement);
        using var result = await SendAsync(HttpMethod.Put, "user", new { password = request.NewPassword }, request.AccessToken, ct);
        return new AuthMessageResponse("Password updated. Sign in with your new password.");
    }

    public async Task<CurrentUserResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        var name = ValidateDisplayName(request.DisplayName);
        _ = await provisioning.EnsureAsync(currentUser.UserId, currentUser.Email, ct);
        var user = await uow.AppUsers.FindAsync([currentUser.UserId], ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "The account was not found.");
        user.DisplayName = name;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return user.ToResponse();
    }

    public async Task<AuthMessageResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        ValidatePassword(request.NewPassword);
        if (string.IsNullOrWhiteSpace(request.CurrentPassword)) throw Invalid("currentPassword", "Current password is required.");
        var user = await provisioning.EnsureAsync(currentUser.UserId, currentUser.Email, ct);
        var session = await RequestTokenAsync("password", new { email = user.Email, password = request.CurrentPassword }, ct);
        if (session.User.Id != currentUser.UserId)
            throw new UnauthorizedException(ErrorCodes.InvalidCredentials, "Current password could not be verified.");
        using var result = await SendAsync(HttpMethod.Put, "user", new { password = request.NewPassword }, session.AccessToken, ct);
        return new AuthMessageResponse("Password updated.");
    }

    public Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["email"] = string.IsNullOrWhiteSpace(request.Email) ? ["Email is required"] : [],
                ["password"] = string.IsNullOrWhiteSpace(request.Password) ? ["Password is required"] : []
            });

        return RequestTokenAsync("password", new { email = request.Email.Trim(), password = request.Password }, ct);
    }

    public Task<AuthTokenResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["refreshToken"] = ["A refresh token is required"]
            });

        return RequestTokenAsync("refresh_token", new { refresh_token = request.RefreshToken }, ct);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var user = await provisioning.EnsureAsync(currentUser.UserId, currentUser.Email, ct);
        return user.ToResponse();
    }

    private async Task<AuthTokenResponse> RequestTokenAsync(string grantType, object body, CancellationToken ct)
    {
        EnsureConfigured();

        using var document = await SendAsync(HttpMethod.Post, $"token?grant_type={grantType}", body, null, ct);
        var root = document.RootElement;

        var accessToken = GetString(root, "access_token")
            ?? throw new UnauthorizedException(ErrorCodes.AuthUnavailable, "Supabase Auth did not return an access token.");

        var subject = root.TryGetProperty("user", out var supabaseUser) ? GetString(supabaseUser, "id") : null;
        if (!Guid.TryParse(subject, out var userId))
            throw new UnauthorizedException(ErrorCodes.AuthUnavailable, "Supabase Auth did not return a user ID.");

        var email = root.TryGetProperty("user", out var u) ? GetString(u, "email") : null;
        var appUser = await provisioning.EnsureAsync(userId, email, ct);

        return TokenResponse(root, appUser);
    }

    private static AuthTokenResponse TokenResponse(JsonElement root, AppUser appUser)
        => new(
            GetString(root, "access_token") ?? throw new UnauthorizedException(ErrorCodes.AuthUnavailable, "Supabase did not return an access token."),
            GetString(root, "token_type") ?? "bearer",
            root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 3600,
            GetString(root, "refresh_token") ?? string.Empty,
            appUser.ToResponse());

    private async Task<JsonDocument> SendAsync(HttpMethod method, string endpoint, object? body, string? bearer, CancellationToken ct)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(method, endpoint);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    throw new BusinessRuleException(ErrorCodes.AuthUnavailable, "Supabase Auth is temporarily unavailable. Try again later.");
                if (endpoint.StartsWith("token", StringComparison.Ordinal) || response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    throw new UnauthorizedException(ErrorCodes.InvalidCredentials, DescribeError(payload));
                throw new BusinessRuleException("AUTH_REQUEST_REJECTED", DescribeError(payload));
            }
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException(ErrorCodes.AuthUnavailable, "Unable to reach Supabase Auth. Try again later.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BusinessRuleException(ErrorCodes.AuthUnavailable, "Supabase Auth did not respond in time. Try again later.");
        }
        catch (JsonException)
        {
            throw new BusinessRuleException(ErrorCodes.AuthUnavailable, "Supabase Auth returned an invalid response.");
        }
    }

    private static Guid ReadUserId(JsonElement user)
        => Guid.TryParse(GetString(user, "id"), out var id) ? id
            : throw new BusinessRuleException(ErrorCodes.AuthUnavailable, "Supabase Auth did not return a valid user ID.");

    private static string ValidateEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw Invalid("email", "Enter a valid email address.");
        return email;
    }

    private static void ValidatePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8 || value.Length > 128)
            throw Invalid("password", "Password must contain between 8 and 128 characters.");
    }

    private static string ValidateDisplayName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120) throw Invalid("displayName", "Display name must contain between 1 and 120 characters.");
        return name;
    }

    private static RequestValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });

    private void EnsureConfigured()
    {
        if (_options.Mode != AuthMode.Supabase)
            throw new BusinessRuleException(ErrorCodes.AuthUnavailable,
                "Sign-in is not required in Dev mode. Switch to Supabase authentication to use this endpoint.");

        if (string.IsNullOrWhiteSpace(_options.ProjectUrl) || string.IsNullOrWhiteSpace(_options.AnonKey))
            throw new BusinessRuleException(ErrorCodes.AuthUnavailable,
                "Auth:ProjectUrl or Auth:AnonKey is missing. Configure them with dotnet user-secrets.");
    }

    /// <summary>GoTrue trả lỗi ở nhiều dạng: {error_description}, {msg}, {message}.</summary>
    private static string DescribeError(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return GetString(root, "error_description")
                   ?? GetString(root, "msg")
                   ?? GetString(root, "message")
                   ?? "Incorrect email or password.";
        }
        catch (JsonException)
        {
            return "Incorrect email or password.";
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
