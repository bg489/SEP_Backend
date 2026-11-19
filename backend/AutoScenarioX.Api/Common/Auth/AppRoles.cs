namespace AutoScenarioX.Api.Common.Auth;

/// <summary>Role nghiệp vụ, khớp CHECK của asx.app_users.role.</summary>
public static class AppRoles
{
    public const string Admin = "ADMIN";
    public const string ScenarioEngineer = "SCENARIO_ENGINEER";
    public const string TestEngineer = "TEST_ENGINEER";
    public const string TestReviewer = "TEST_REVIEWER";

    public static readonly IReadOnlyList<string> All =
        [Admin, ScenarioEngineer, TestEngineer, TestReviewer];
}

/// <summary>Tên policy phân quyền dùng trong [Authorize(Policy = ...)].</summary>
public static class AuthPolicies
{
    /// <summary>Được phép quản lý scenario (Flow 1): Scenario Engineer hoặc Admin.</summary>
    public const string ManageScenarios = "ManageScenarios";

    /// <summary>Được phép nộp Telemetry của simulation run để Oracle chấm (runner của Flow 2 đăng nhập bằng tài khoản này): Test Engineer hoặc Admin.</summary>
    public const string RunCampaigns = "RunCampaigns";

    public const string ReviewResults = "ReviewResults";
    public const string AdminOnly = "AdminOnly";
}

/// <summary>Tên claim do API tự gắn sau khi xác thực (không phải claim gốc của Supabase).</summary>
public static class AsxClaimTypes
{
    /// <summary>Role nghiệp vụ lấy từ asx.app_users, KHÁC claim "role" của Supabase (luôn là "authenticated").</summary>
    public const string AppRole = "asx_role";

    public const string Subject = "sub";
    public const string Email = "email";
}
