namespace AutoScenarioX.Api.Common.Constants;

/// <summary>
/// Loại đối tượng ghi trong asx.audit_logs.entity_type.
/// PHẢI khớp CHECK constraint trong database/migrations/001_flow1_scenario_management.sql.
/// </summary>
public static class AuditEntityTypes
{
    public const string Scenario = "SCENARIO";
    public const string Draft = "DRAFT";
    public const string Version = "VERSION";
    public const string Import = "IMPORT";
    public const string VariantBatch = "VARIANT_BATCH";
    public const string MapMetadata = "MAP_METADATA";
}

/// <summary>
/// Hành động ghi trong asx.audit_logs.action.
/// PHẢI khớp CHECK ck_audit_logs_action (database/migrations/113_audit_update_profile.sql, thay bản inline của 001).
/// </summary>
public static class AuditActions
{
    public const string Create = "CREATE";
    public const string UpdateDraft = "UPDATE_DRAFT";
    public const string DiscardDraft = "DISCARD_DRAFT";
    public const string Validate = "VALIDATE";
    public const string Publish = "PUBLISH";
    public const string Clone = "CLONE";
    public const string Import = "IMPORT";
    public const string Export = "EXPORT";
    public const string GenerateVariants = "GENERATE_VARIANTS";
    public const string Deprecate = "DEPRECATE";
    public const string Archive = "ARCHIVE";
    public const string Restore = "RESTORE";
    public const string Tag = "TAG";
    public const string Untag = "UNTAG";
    /// <summary>Sửa hồ sơ scenario (tên, mô tả, category) - từ migration 106.</summary>
    public const string UpdateProfile = "UPDATE_PROFILE";
}
