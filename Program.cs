using System.Text;
using System.Threading.RateLimiting;
using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Data;
using AutoScenarioX.AuthApi.Models;
using AutoScenarioX.AuthApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AuthDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Set ConnectionStrings:AuthDatabase to the PostgreSQL 18 autoscenariox database. " +
        "For local development, run '.\\scripts\\Set-LocalSecrets.ps1' from the Backend folder, " +
        "enter your PostgreSQL credentials, then run 'dotnet run' again. " +
        "Use environment variables in deployment.");
}
var postgresConnection = new NpgsqlConnectionStringBuilder(connectionString);
if (!string.Equals(postgresConnection.Database, "autoscenariox", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "The authentication API must use the PostgreSQL database named 'autoscenariox'.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "AutoScenarioX.AuthApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "AutoScenarioX.Frontend";
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
}

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<WorkflowDocumentRepository>();
builder.Services.AddScoped<AuditLogWriter>();
builder.Services.AddScoped<ScenarioRunCallbackWriter>();
builder.Services.AddSingleton<CarlaRuntime>();
builder.Services.AddSingleton<ScenarioExecutionWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<ScenarioExecutionWorker>());

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(RoleNames.ScenarioEngineer, policy => policy.RequireRole(RoleNames.ScenarioEngineer));
    options.AddPolicy(RoleNames.TestEngineer, policy => policy.RequireRole(RoleNames.TestEngineer));
    options.AddPolicy(RoleNames.TestReviewer, policy => policy.RequireRole(RoleNames.TestReviewer));
    options.AddPolicy(RoleNames.Administrator, policy => policy.RequireRole(RoleNames.Administrator));
});

var allowedOrigins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        }));
});

builder.Services.AddScoped<IJwtTokenService>(services =>
    new JwtTokenService(
        services.GetRequiredService<UserManager<ApplicationUser>>(),
        jwtIssuer,
        jwtAudience,
        jwtSigningKey,
        TimeSpan.FromMinutes(builder.Configuration.GetValue("Jwt:AccessTokenMinutes", 20))));

var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await database.Database.OpenConnectionAsync(app.Lifetime.ApplicationStopping);
    var serverMajorVersion = database.Database.GetDbConnection().ServerVersion.Split('.')[0];
    if (!string.Equals(serverMajorVersion, "18", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"AutoScenarioX authentication requires PostgreSQL 18; connected server reports {serverMajorVersion}.");
    }
    await database.Database.CloseConnectionAsync();
    await database.Database.MigrateAsync();

    await IdentityRoleSeeder.SeedAsync(
        scope.ServiceProvider,
        builder.Configuration,
        app.Logger,
        app.Lifetime.ApplicationStopping);
}

app.Run();
