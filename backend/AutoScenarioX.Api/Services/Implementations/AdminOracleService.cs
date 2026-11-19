using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class AdminOracleService(IOracleConfigRepository configs, IAdministrationRepository admin, ICurrentUser user)
    : IAdminOracleService
{
    public async Task<IReadOnlyList<AdminOracleConfigResponse>> ListAsync(CancellationToken ct = default)
    {
        RequireAdmin();
        var active = (await configs.GetLatestAsync(ct))?.Id;
        return (await configs.ListAsync(ct)).Select(x => ToResponse(x, x.Id == active)).ToList();
    }

    public async Task<AdminOracleConfigResponse> CreateAsync(CreateOracleConfigRequest request, CancellationToken ct = default)
    {
        RequireAdmin();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120) errors["name"] = ["Use 1 to 120 characters."];
        if (request.Description?.Length > 1000) errors["description"] = ["Use at most 1000 characters."];
        if (request.SpeedingToleranceKmh is < 0 or > 100) errors["speedingToleranceKmh"] = ["Must be between 0 and 100."];
        if (request.SpeedingMinDurationS is <= 0 or > 3600) errors["speedingMinDurationS"] = ["Must be greater than 0 and at most 3600."];
        if (request.FreezingMaxStillS is <= 0 or > 3600) errors["freezingMaxStillS"] = ["Must be greater than 0 and at most 3600."];
        if (request.TtcThresholdS is <= 0 or > 60) errors["ttcThresholdS"] = ["Must be greater than 0 and at most 60."];
        if (request.RouteGoalToleranceM is <= 0 or > 100) errors["routeGoalToleranceM"] = ["Must be greater than 0 and at most 100."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var config = await admin.CreateOracleConfigAsync(new OracleConfig
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), Description = request.Description?.Trim(),
            SpeedingToleranceKmh = request.SpeedingToleranceKmh, SpeedingMinDurationS = request.SpeedingMinDurationS,
            FreezingMaxStillS = request.FreezingMaxStillS, TtcThresholdS = request.TtcThresholdS,
            RouteGoalToleranceM = request.RouteGoalToleranceM, CreatedBy = user.UserId, CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
        return ToResponse(config, false);
    }

    public async Task<SystemSettingsResponse> ActivateAsync(Guid id, CancellationToken ct = default)
    {
        RequireAdmin();
        return ToSettings(await admin.ActivateOracleAsync(id, user.UserId, ct));
    }

    public async Task<SystemSettingsResponse> SettingsAsync(CancellationToken ct = default)
    {
        RequireAdmin();
        return ToSettings(await admin.SettingsAsync(ct));
    }

    public async Task<SystemSettingsResponse> UpdateSettingsAsync(UpdateSystemSettingsRequest request, CancellationToken ct = default)
    {
        RequireAdmin();
        var errors = new Dictionary<string, string[]>();
        if (request.SimilarityWarningPercent is < 0 or > 100) errors["similarityWarningPercent"] = ["Must be between 0 and 100."];
        if (request.SimilarityBlockPercent < request.SimilarityWarningPercent || request.SimilarityBlockPercent > 100)
            errors["similarityBlockPercent"] = ["Must be at least the warning threshold and at most 100."];
        if (request.RetentionDays is < 1 or > 3650) errors["retentionDays"] = ["Must be between 1 and 3650 days."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var settings = await admin.SettingsAsync(ct);
        settings.SimilarityWarningPercent = request.SimilarityWarningPercent;
        settings.SimilarityBlockPercent = request.SimilarityBlockPercent;
        settings.RetentionDays = request.RetentionDays;
        settings.RetentionEnabled = request.RetentionEnabled;
        settings.UpdatedBy = user.UserId;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        return ToSettings(await admin.SaveSettingsAsync(settings, ct));
    }

    public async Task<IReadOnlyList<AuditEntryResponse>> AuditAsync(int page, int pageSize, CancellationToken ct = default)
    {
        RequireAdmin();
        if (page < 1 || pageSize is < 1 or > 100)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["page"] = ["Page must be positive; pageSize must be 1 to 100."] });
        return (await admin.AuditAsync(page, pageSize, ct)).Select(x => new AuditEntryResponse(
            x.Id, x.Action, x.EntityType, x.EntityId.ToString(), x.ActorId, x.CreatedAt, x.Details)).ToList();
    }

    private void RequireAdmin()
    {
        if (!user.IsAuthenticated) throw new UnauthorizedException("AUTH_REQUIRED", "Authentication is required.");
        if (user.Role != AppRoles.Admin) throw new ForbiddenException("ADMIN_REQUIRED", "Only administrators can change system configuration.");
    }

    private static AdminOracleConfigResponse ToResponse(OracleConfig x, bool active) => new(
        x.Id, x.Version, x.Name, x.Description, x.SpeedingToleranceKmh, x.SpeedingMinDurationS,
        x.FreezingMaxStillS, x.TtcThresholdS, x.RouteGoalToleranceM, x.CreatedAt, active);

    private static SystemSettingsResponse ToSettings(SystemSettings x) => new(
        x.SimilarityWarningPercent, x.SimilarityBlockPercent, x.RetentionDays, x.RetentionEnabled,
        x.ActiveOracleConfigId, x.UpdatedAt,
        "Retention policy is saved. Referenced run evidence is protected; no automatic artifact deletion is performed.");
}
