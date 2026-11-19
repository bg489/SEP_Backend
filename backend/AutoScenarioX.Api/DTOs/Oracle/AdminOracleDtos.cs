namespace AutoScenarioX.Api.DTOs.Oracle;

public sealed record CreateOracleConfigRequest(
    string Name, string? Description, decimal SpeedingToleranceKmh = 5,
    decimal SpeedingMinDurationS = 2, decimal FreezingMaxStillS = 10,
    decimal TtcThresholdS = 2, decimal RouteGoalToleranceM = 5);

public sealed record AdminOracleConfigResponse(
    Guid Id, int Version, string Name, string? Description,
    decimal SpeedingToleranceKmh, decimal SpeedingMinDurationS, decimal FreezingMaxStillS,
    decimal TtcThresholdS, decimal RouteGoalToleranceM, DateTimeOffset CreatedAt, bool IsActive);

public sealed record UpdateSystemSettingsRequest(
    decimal SimilarityWarningPercent, decimal SimilarityBlockPercent,
    int RetentionDays, bool RetentionEnabled);

public sealed record SystemSettingsResponse(
    decimal SimilarityWarningPercent, decimal SimilarityBlockPercent,
    int RetentionDays, bool RetentionEnabled, Guid? ActiveOracleConfigId,
    DateTimeOffset UpdatedAt, string RetentionNote);

public sealed record AuditEntryResponse(long Id, string Action, string EntityType,
    string EntityId, Guid? ActorId, DateTimeOffset CreatedAt, string? Details);
