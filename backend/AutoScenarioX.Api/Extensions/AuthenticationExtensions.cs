using System.Security.Claims;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace AutoScenarioX.Api.Extensions;

/// <summary>
/// Xác thực và phân quyền.
/// Auth:Mode = Dev      -> DevAuthenticationHandler gán sẵn user trong section DevAuth, không cần token.
/// Auth:Mode = Supabase -> xác thực JWT của Supabase, role lấy từ asx.app_users lúc token hợp lệ.
/// Hai chế độ dùng CHUNG policy nên [Authorize] hoạt động giống nhau.
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<DevAuthOptions>(configuration.GetSection(DevAuthOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IAuthService, SupabaseAuthService>();
        services.AddScoped<IAdminUsersService, AdminUsersService>();

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        // Dev cho mọi request đăng nhập sẵn: quên đặt Auth__Mode khi deploy thì API mở cho cả Internet.
        if (auth.Mode == AuthMode.Dev && !environment.IsDevelopment())
            throw new InvalidOperationException(
                $"Auth:Mode=Dev chỉ dùng ở môi trường Development (hiện là '{environment.EnvironmentName}'). " +
                "Đặt Auth__Mode=Supabase khi deploy.");
        AddSupabaseHttpClients(services, auth);

        if (auth.Mode == AuthMode.Supabase)
            AddSupabaseJwt(services, auth);
        else
            services.AddAuthentication(DevAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, null);

        services.AddAuthorization(o =>
        {
            o.AddPolicy(AuthPolicies.ManageScenarios, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AsxClaimTypes.AppRole, AppRoles.ScenarioEngineer, AppRoles.Admin));

            o.AddPolicy(AuthPolicies.RunCampaigns, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AsxClaimTypes.AppRole, AppRoles.TestEngineer, AppRoles.Admin));

            o.AddPolicy(AuthPolicies.ReviewResults, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AsxClaimTypes.AppRole, AppRoles.TestReviewer, AppRoles.Admin));

            o.AddPolicy(AuthPolicies.AdminOnly, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AsxClaimTypes.AppRole, AppRoles.Admin));

            // Mặc định mọi endpoint đều cần đăng nhập; mở bằng [AllowAnonymous]
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        return services;
    }

    private static void AddSupabaseHttpClients(IServiceCollection services, AuthOptions auth)
    {
        var configured = !string.IsNullOrWhiteSpace(auth.ProjectUrl);

        services.AddHttpClient(SupabaseAuthService.HttpClientName, client =>
        {
            if (configured) client.BaseAddress = new Uri($"{auth.Issuer}/");
            if (!string.IsNullOrWhiteSpace(auth.AnonKey)) client.DefaultRequestHeaders.Add("apikey", auth.AnonKey);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient(SupabaseSigningKeys.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
    }

    private static void AddSupabaseJwt(IServiceCollection services, AuthOptions auth)
    {
        services.AddSingleton<SupabaseSigningKeys>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SupabaseSigningKeys>((options, signingKeys) =>
            {
                options.MapInboundClaims = false;          // giữ nguyên tên claim gốc: sub, email
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.Issuer,
                    ValidateAudience = true,
                    ValidAudience = auth.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = signingKeys.Resolve,
                    NameClaimType = AsxClaimTypes.Subject,
                    RoleClaimType = AsxClaimTypes.AppRole,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = OnTokenValidatedAsync
                };
            });
    }

    /// <summary>
    /// Token hợp lệ rồi thì đối chiếu với asx.app_users: lấy role nghiệp vụ (tạo dòng mới nếu được phép)
    /// và gắn thành claim để policy phân quyền dùng.
    /// Claim "role" của Supabase luôn là "authenticated" nên KHÔNG dùng được cho việc này.
    /// </summary>
    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            context.Fail("Token không có danh tính hợp lệ.");
            return;
        }

        var subject = context.Principal.FindFirstValue(AsxClaimTypes.Subject);
        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("Token thiếu claim 'sub' dạng UUID.");
            return;
        }

        var provisioning = context.HttpContext.RequestServices.GetRequiredService<IUserProvisioningService>();
        var user = await provisioning.EnsureAsync(
            userId,
            context.Principal.FindFirstValue(AsxClaimTypes.Email),
            context.HttpContext.RequestAborted);

        identity.AddClaim(new Claim(AsxClaimTypes.AppRole, user.Role));
    }
}
