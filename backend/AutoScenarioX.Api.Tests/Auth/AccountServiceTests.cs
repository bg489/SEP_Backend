using System.Net;
using System.Text;
using System.Text.Json;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Auth;

public class AccountServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly IUserProvisioningService _provisioning = Substitute.For<IUserProvisioningService>();
    private readonly ICurrentUser _user = FakeUnitOfWork.User();
    private readonly QueueHandler _handler = new();
    private readonly SupabaseAuthService _service;
    private readonly Guid _id = Guid.NewGuid();

    public AccountServiceTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(SupabaseAuthService.HttpClientName).Returns(new HttpClient(_handler) { BaseAddress = new Uri("https://test.invalid/auth/v1/") });
        _service = new SupabaseAuthService(factory, _provisioning, _user,
            Options.Create(new AuthOptions { Mode = AuthMode.Supabase, ProjectUrl = "https://test.invalid", AnonKey = "test-public-key", PasswordResetRedirectUrl = "https://web.invalid/reset-password" }), _uow);
        _user.UserId.Returns(_id);
        _user.Email.Returns("user@example.com");
        _provisioning.EnsureAsync(_id, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(User());
    }

    private AppUser User(string role = AppRoles.TestEngineer) => new() { Id = _id, Email = "user@example.com", DisplayName = "User", Role = role, IsActive = true };
    private static RegisterRequest Registration(string role = AppRoles.TestEngineer) => new() { Email = " User@Example.com ", Password = "password-123", DisplayName = " Example User ", Role = role };
    private string TokenJson() => JsonSerializer.Serialize(new { access_token = "access", refresh_token = "refresh", expires_in = 1800, user = new { id = _id, email = "user@example.com" } });

    [Theory]
    [InlineData(AppRoles.ScenarioEngineer)]
    [InlineData(AppRoles.TestEngineer)]
    [InlineData(AppRoles.TestReviewer)]
    public async Task Registration_persists_selected_role_and_supports_confirmation_without_session(string role)
    {
        _handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = _id, identities = new[] { new { id = _id } } }));
        var result = await _service.RegisterAsync(Registration(role));
        Assert.True(result.RequiresEmailConfirmation);
        Assert.Null(result.Session);
        Assert.Equal(role, result.User.Role);
        Assert.Equal("user@example.com", result.User.Email);
        _uow.AppUsers.Received(1).Add(Arg.Is<AppUser>(u => u.Id == _id && u.Role == role && u.DisplayName == "Example User"));
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("/auth/v1/signup", request.Path);
        using var json = JsonDocument.Parse(request.Body);
        Assert.False(json.RootElement.GetProperty("data").TryGetProperty("role", out _));
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Registration_returns_actual_session_when_confirmation_is_disabled()
    {
        _handler.Enqueue(HttpStatusCode.OK, TokenJson());
        var result = await _service.RegisterAsync(Registration());
        Assert.False(result.RequiresEmailConfirmation);
        Assert.Equal("access", result.Session!.AccessToken);
        Assert.Equal(AppRoles.TestEngineer, result.Session.User.Role);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task Admin_or_unknown_registration_role_is_rejected_before_http(string role)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => _service.RegisterAsync(Registration(role)));
        Assert.Empty(_handler.Requests);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Existing_email_is_rejected_before_supabase_signup()
    {
        _uow.AppUsers.EmailExistsAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(true);
        await Assert.ThrowsAsync<ConflictException>(() => _service.RegisterAsync(Registration()));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Supabase_obfuscated_duplicate_is_not_provisioned()
    {
        _handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = _id, identities = Array.Empty<object>() }));
        await Assert.ThrowsAsync<ConflictException>(() => _service.RegisterAsync(Registration()));
        Assert.Equal(0, _uow.SaveCount);
    }

    [Theory]
    [InlineData("not-email", "password-123")]
    [InlineData("u@example.com", "short")]
    public async Task Invalid_registration_data_is_not_sent_to_supabase(string email, string password)
    {
        var request = Registration(); request.Email = email; request.Password = password;
        await Assert.ThrowsAsync<RequestValidationException>(() => _service.RegisterAsync(request));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Login_returns_database_role_instead_of_supabase_metadata()
    {
        _handler.Enqueue(HttpStatusCode.OK, TokenJson());
        var result = await _service.LoginAsync(new LoginRequest { Email = "user@example.com", Password = "anything" });
        Assert.Equal(AppRoles.TestEngineer, result.User.Role);
        Assert.Equal(1800, result.ExpiresIn);
        Assert.Contains("grant_type=password", Assert.Single(_handler.Requests).Path);
    }

    [Fact]
    public async Task Invalid_credentials_and_refresh_are_unauthorized()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, "{\"error_description\":\"Invalid login credentials\"}");
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.LoginAsync(new LoginRequest { Email = "u@example.com", Password = "wrong" }));
        _handler.Enqueue(HttpStatusCode.BadRequest, "{\"msg\":\"Invalid refresh token\"}");
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync(new RefreshTokenRequest { RefreshToken = "expired" }));
    }

    [Fact]
    public async Task Disabled_database_account_cannot_receive_session()
    {
        _handler.Enqueue(HttpStatusCode.OK, TokenJson());
        _provisioning.EnsureAsync(_id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<AppUser>>(_ => throw new ForbiddenException("FORBIDDEN", "disabled"));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.LoginAsync(new LoginRequest { Email = "user@example.com", Password = "password" }));
    }

    [Fact]
    public async Task Recovery_uses_server_configured_redirect_and_generic_message()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");
        var result = await _service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "missing@example.com" });
        Assert.Contains("If an account exists", result.Message);
        Assert.Contains("recover?redirect_to=https%3A%2F%2Fweb.invalid", Assert.Single(_handler.Requests).Path);
    }

    [Fact]
    public async Task Reset_verifies_recovery_token_remotely_before_updating_password()
    {
        _handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = _id }));
        _handler.Enqueue(HttpStatusCode.OK, "{}");
        await _service.ResetPasswordAsync(new ResetPasswordRequest { AccessToken = "recovery", NewPassword = "new-password-123" });
        Assert.Equal(new[] { "GET", "PUT" }, _handler.Requests.Select(r => r.Method));
        Assert.All(_handler.Requests, r => Assert.Equal("Bearer recovery", r.Authorization));
    }

    [Fact]
    public async Task Expired_recovery_token_never_updates_password()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized, "{\"msg\":\"expired\"}");
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ResetPasswordAsync(new ResetPasswordRequest { AccessToken = "expired", NewPassword = "new-password-123" }));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Profile_updates_only_current_account_and_leaves_role_unchanged()
    {
        var user = User();
        _uow.AppUsers.FindAsync(Arg.Is<object[]>(keys => (Guid)keys[0] == _id), Arg.Any<CancellationToken>()).Returns(user);
        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = " Updated " });
        Assert.Equal("Updated", result.DisplayName);
        Assert.Equal(AppRoles.TestEngineer, result.Role);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Change_password_checks_current_password_then_updates_supabase()
    {
        _handler.Enqueue(HttpStatusCode.OK, TokenJson());
        _handler.Enqueue(HttpStatusCode.OK, "{}");
        await _service.ChangePasswordAsync(new ChangePasswordRequest { CurrentPassword = "old-password", NewPassword = "new-password-123" });
        Assert.Equal("POST", _handler.Requests[0].Method);
        Assert.Equal("PUT", _handler.Requests[1].Method);
        Assert.Equal("Bearer access", _handler.Requests[1].Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Provider_failure_is_explicit_and_does_not_write_database(HttpStatusCode status)
    {
        _handler.Enqueue(status, "<html>down</html>");
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => _service.RegisterAsync(Registration()));
        Assert.Equal("AUTH_UNAVAILABLE", error.Code);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Network_failure_has_no_sensitive_provider_details()
    {
        _handler.NetworkError = true;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => _service.RegisterAsync(Registration()));
        Assert.Equal("AUTH_UNAVAILABLE", error.Code);
        Assert.DoesNotContain("secret", error.Message);
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
        public List<(string Method, string Path, string Body, string? Authorization)> Requests { get; } = [];
        public bool NetworkError { get; set; }
        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct), request.Headers.Authorization?.ToString()));
            if (NetworkError) throw new HttpRequestException("secret provider details");
            var response = _responses.Dequeue();
            return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
        }
    }
}
