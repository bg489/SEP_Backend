namespace AutoScenarioX.Api.Common.Constants;

/// <summary>
/// Mã lỗi trả về trong ProblemDetails (trường "code"). Frontend dựa vào mã này để hiện thông báo.
/// Quy ước: VIẾT_HOA_GẠCH_DƯỚI, không đổi mã đã phát hành.
/// </summary>
public static class ErrorCodes
{
    // Chung
    public const string InternalError = "INTERNAL_ERROR";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AuthUnavailable = "AUTH_UNAVAILABLE";
    public const string UserNotProvisioned = "USER_NOT_PROVISIONED";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";

    // Dữ liệu (dịch từ lỗi PostgreSQL)
    public const string DuplicateData = "DUPLICATE_DATA";
    public const string ReferenceNotFound = "REFERENCE_NOT_FOUND";
    public const string ConstraintViolation = "CONSTRAINT_VIOLATION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";

    // Flow 1 - Scenario
    public const string ScenarioNotFound = "SCENARIO_NOT_FOUND";
    public const string ScenarioArchived = "SCENARIO_ARCHIVED";
    public const string DraftNotFound = "DRAFT_NOT_FOUND";
    public const string DraftAlreadyExists = "DRAFT_ALREADY_EXISTS";
    public const string VersionNotFound = "VERSION_NOT_FOUND";
    public const string VersionImmutable = "VERSION_IMMUTABLE";
    public const string ValidationRequired = "VALIDATION_REQUIRED";
    public const string ContentChangedSinceValidation = "CONTENT_CHANGED_SINCE_VALIDATION";
    public const string DuplicateVersion = "DUPLICATE_VERSION";
    public const string VersionAlreadyDeprecated = "VERSION_ALREADY_DEPRECATED";
    public const string ScenarioNotArchived = "SCENARIO_NOT_ARCHIVED";
    public const string NoVersionToClone = "NO_VERSION_TO_CLONE";

    // Flow 1 - Import / Export (B11)
    public const string ImportFormatUnsupported = "IMPORT_FORMAT_UNSUPPORTED";
    public const string ImportParseFailed = "IMPORT_PARSE_FAILED";
    public const string ImportValidationFailed = "IMPORT_VALIDATION_FAILED";
    public const string ImportFileTooLarge = "IMPORT_FILE_TOO_LARGE";
    public const string ImportFileEmpty = "IMPORT_FILE_EMPTY";

    // Flow 1 - Sinh biến thể (B12)
    public const string VariantBatchNotFound = "VARIANT_BATCH_NOT_FOUND";
    public const string VariantBaseNotReady = "VARIANT_BASE_NOT_READY";
    public const string VariantValidationFailed = "VARIANT_VALIDATION_FAILED";

    // Flow 3 - Oracle
    public const string RunNotFound = "RUN_NOT_FOUND";
    public const string RunNotStarted = "RUN_NOT_STARTED";
    public const string RunNotClaimed = "RUN_NOT_CLAIMED";
    public const string RunNotRequeueable = "RUN_NOT_REQUEUEABLE";
    public const string InfraRetryExhausted = "INFRA_RETRY_EXHAUSTED";
    public const string VideoNotFound = "VIDEO_NOT_FOUND";
    public const string TelemetryAlreadyReceived = "TELEMETRY_ALREADY_RECEIVED";
    public const string TelemetryRunMismatch = "TELEMETRY_RUN_MISMATCH";
    public const string TelemetryTooLarge = "TELEMETRY_TOO_LARGE";
    public const string OracleConfigNotFound = "ORACLE_CONFIG_NOT_FOUND";

    // Dữ liệu tham chiếu
    public const string MapNotFound = "MAP_NOT_FOUND";
}
