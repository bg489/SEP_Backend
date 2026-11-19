namespace AutoScenarioX.Api.Common.Constants;

/// <summary>
/// Phiên bản bộ validator, ghi vào asx.validation_reports.validator_version.
/// TĂNG khi luật validate thay đổi để biết report cũ được sinh bởi bộ luật nào.
/// </summary>
public static class ValidatorVersion
{
    /// <remarks>
    /// csharp-1.1 (2026-10-09): thêm 4 luật tầng 3 về vị trí, chồng spawn, route và trigger.
    /// csharp-1.2 (2026-10-20): RouteFeasibilityRule tính theo ego target_speed_kmh, TriggerAtStartRule xét trigger TTC.
    /// </remarks>
    public const string Current = "csharp-1.2";
}
