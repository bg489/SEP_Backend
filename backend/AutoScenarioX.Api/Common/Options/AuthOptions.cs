namespace AutoScenarioX.Api.Common.Options;

/// <summary>Cách xác thực người gọi API.</summary>
public enum AuthMode
{
    /// <summary>Không cần token: mọi request coi như user dev trong DevAuth. CHỈ DÙNG KHI DEV.</summary>
    Dev,

    /// <summary>Xác thực JWT do Supabase Auth phát hành.</summary>
    Supabase
}

/// <summary>Section "Auth" trong appsettings.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Dev;

    /// <summary>URL dự án Supabase, ví dụ https://abcdxyz.supabase.co (không có dấu / cuối).</summary>
    public string ProjectUrl { get; set; } = string.Empty;

    /// <summary>anon key của dự án - chỉ dùng cho endpoint đăng nhập/làm mới token. Đặt bằng user-secrets.</summary>
    public string AnonKey { get; set; } = string.Empty;

    /// <summary>Supabase luôn phát hành token với aud = "authenticated".</summary>
    public string Audience { get; set; } = "authenticated";

    /// <summary>
    /// Khoá HS256 cũ của Supabase (Settings -> API -> JWT Secret).
    /// Chỉ cần khi dự án chưa bật JWT bất đối xứng (JWKS rỗng).
    /// </summary>
    public string? LegacyJwtSecret { get; set; }

    /// <summary>Tự tạo dòng trong asx.app_users khi user Supabase gọi API lần đầu.</summary>
    public bool AutoProvisionUsers { get; set; } = true;

    /// <summary>Role gán cho user mới tự tạo (xem AppRoles).</summary>
    public string DefaultRole { get; set; } = "SCENARIO_ENGINEER";

    /// <summary>Optional allowlisted frontend URL configured in Supabase for recovery links.</summary>
    public string? PasswordResetRedirectUrl { get; set; }

    /// <summary>Gốc của GoTrue: https://&lt;ref&gt;.supabase.co/auth/v1 - cũng là giá trị claim "iss".</summary>
    public string Issuer => $"{ProjectUrl.TrimEnd('/')}/auth/v1";

    public string JwksUri => $"{Issuer}/.well-known/jwks.json";

    public string TokenEndpoint => $"{Issuer}/token";
}
