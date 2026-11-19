using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Middlewares;
using AutoScenarioX.Api.Repositories.Implementations;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Formats;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Validators.ScenarioContent;
using AutoScenarioX.Api.Validators.ScenarioContent.Rules;
using AutoScenarioX.Api.Validators.ScenarioContent.Rules.Business;
using AutoScenarioX.Api.Validators.ScenarioContent.Rules.Carla;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

namespace AutoScenarioX.Api.Extensions;

/// <summary>
/// Gom việc đăng ký Dependency Injection theo từng lớp để Program.cs gọn.
/// Thêm repository / service mới: đăng ký ở đúng method tương ứng.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Lớp Data Access: DbContext, Repository, UnitOfWork.</summary>
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Supabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Thiếu ConnectionStrings:Supabase. Đặt bằng lệnh: " +
                "dotnet user-secrets set \"ConnectionStrings:Supabase\" \"<chuỗi kết nối>\"");

        services.AddDbContext<AsxDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());      // LatestVersionNumber <-> latest_version_number

        services.AddScoped<IScenarioRepository, ScenarioRepository>();
        services.AddScoped<IScenarioVersionRepository, ScenarioVersionRepository>();
        services.AddScoped<IScenarioDraftRepository, ScenarioDraftRepository>();
        services.AddScoped<IMapMetadataRepository, MapMetadataRepository>();
        services.AddScoped<IValidationReportRepository, ValidationReportRepository>();
        services.AddScoped<IImportRecordRepository, ImportRecordRepository>();
        services.AddScoped<IVariantBatchRepository, VariantBatchRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IOracleConfigRepository, OracleConfigRepository>();
        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IFailureAnalysisRepository, FailureAnalysisRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IWorkerRepository, WorkerRepository>();
        services.AddScoped<IAdministrationRepository, AdministrationRepository>();
        services.AddScoped<IPrioritizationHistoryRepository, PrioritizationHistoryRepository>();
        return services;
    }

    /// <summary>Lớp Business Logic: Service.</summary>
    public static IServiceCollection AddBusinessServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ScenarioOptions>(configuration.GetSection(ScenarioOptions.SectionName));
        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));

        services.AddScoped<IScenarioService, ScenarioService>();
        services.AddScoped<IScenarioVersionService, ScenarioVersionService>();
        services.AddScoped<IScenarioDraftService, ScenarioDraftService>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();
        services.AddScoped<IScenarioValidationService, ScenarioValidationService>();
        services.AddScoped<IScenarioImportService, ScenarioImportService>();
        services.AddScoped<IScenarioExportService, ScenarioExportService>();
        services.AddScoped<IScenarioVariantService, ScenarioVariantService>();
        services.AddScoped<IScenarioHandoffService, ScenarioHandoffService>();
        services.AddScoped<IPrioritizationService, PrioritizationService>();
        services.AddScoped<ICampaignService, CampaignService>();
        services.AddScoped<IOracleService, OracleService>();
        services.AddHttpClient<IRunArtifactStore, RunArtifactStore>();
        services.AddScoped<IFailureAnalysisService, FailureAnalysisService>();
        services.AddScoped<IScenarioTagService, ScenarioTagService>();
        services.AddScoped<ICoverageService, CoverageService>();
        services.AddScoped<IScenarioHistoryService, ScenarioHistoryService>();
        services.AddScoped<IWorkerManagementService, WorkerManagementService>();
        services.AddScoped<IAdminOracleService, AdminOracleService>();
        services.AddScoped<ISimilarityService, SimilarityService>();

        services.AddValidationRules();
        services.AddFormatAdapters();
        return services;
    }

    /// <summary>
    /// Adapter import/export.
    /// THÊM ĐỊNH DẠNG MỚI: tạo class cài IScenarioFormatAdapter rồi thêm một dòng AddScoped ở đây,
    /// KHÔNG sửa import/export service.
    /// </summary>
    private static IServiceCollection AddFormatAdapters(this IServiceCollection services)
    {
        services.AddScoped<IScenarioFormatAdapter, JsonScenarioFormatAdapter>();
        services.AddScoped<IScenarioFormatAdapter, YamlScenarioFormatAdapter>();
        services.AddScoped<IScenarioFormatAdapter, OpenScenarioFormatAdapter>();
        services.AddScoped<IScenarioFormatRegistry, ScenarioFormatRegistry>();
        return services;
    }

    /// <summary>
    /// Validate nội dung scenario: bộ chạy 3 tầng + từng luật.
    /// THÊM LUẬT MỚI: tạo class cài IScenarioValidationRule rồi thêm một dòng AddScoped ở đây,
    /// KHÔNG sửa ScenarioValidator.
    /// </summary>
    private static IServiceCollection AddValidationRules(this IServiceCollection services)
    {
        services.AddSingleton<ICanonicalJsonHasher, CanonicalJsonHasher>();
        services.AddScoped<IScenarioValidator, ScenarioValidator>();

        // Tầng 1 - cấu trúc và miền giá trị
        services.AddScoped<IScenarioValidationRule, SchemaValidationRule>();

        // Tầng 2 - luật nghiệp vụ
        services.AddScoped<IScenarioValidationRule, ParameterRangeRule>();
        services.AddScoped<IScenarioValidationRule, ParamReferenceRule>();
        services.AddScoped<IScenarioValidationRule, ParamTargetRangeRule>();
        services.AddScoped<IScenarioValidationRule, ActorReferenceRule>();
        services.AddScoped<IScenarioValidationRule, TriggerTimeoutRule>();
        services.AddScoped<IScenarioValidationRule, EventChainRule>();
        services.AddScoped<IScenarioValidationRule, ScriptedActorAutopilotRule>();
        services.AddScoped<IScenarioValidationRule, StartGoalRule>();

        // Tầng 3 - đối chiếu map metadata của CARLA
        services.AddScoped<IScenarioValidationRule, MapExistsRule>();
        services.AddScoped<IScenarioValidationRule, SpawnPointIndexRule>();
        services.AddScoped<IScenarioValidationRule, NpcCountRule>();
        services.AddScoped<IScenarioValidationRule, MapPositionRule>();
        services.AddScoped<IScenarioValidationRule, SpawnOverlapRule>();
        services.AddScoped<IScenarioValidationRule, RouteFeasibilityRule>();
        services.AddScoped<IScenarioValidationRule, TriggerAtStartRule>();

        return services;
    }

    /// <summary>Lớp Presentation: controller, xử lý lỗi, CORS, OpenAPI, người dùng hiện tại.</summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Swagger (tài liệu + giao diện thử API) - .NET 8 dùng Swashbuckle
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "AutoScenarioX API",
                Version = "v1",
                Description = "Flow 1–5: scenarios, prioritization, campaigns, execution, failure analysis and administration"
            });
            var xmlFile = Path.Combine(AppContext.BaseDirectory, "AutoScenarioX.Api.xml");
            if (File.Exists(xmlFile)) options.IncludeXmlComments(xmlFile);

            // Nút "Authorize" trong Swagger: dán access token lấy từ POST /api/v1/auth/login
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Dán access token (không cần chữ 'Bearer').",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            options.AddSecurityDefinition("Bearer", scheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
        });

        var cors = configuration.GetSection(FrontendCorsOptions.SectionName).Get<FrontendCorsOptions>()
                   ?? new FrontendCorsOptions();
        services.AddCors(options => options.AddPolicy(FrontendCorsOptions.PolicyName, policy => policy
            .WithOrigins(cors.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("ETag")));

        return services;
    }
}
