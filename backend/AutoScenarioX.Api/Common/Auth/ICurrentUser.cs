namespace AutoScenarioX.Api.Common.Auth;

/// <summary>
/// Người dùng đang gọi API. Service dùng interface này, KHÔNG đọc HttpContext trực tiếp.
/// Hiện tại: DevCurrentUser. Sau này: thay bằng lớp đọc JWT của Supabase Auth,
/// không phải sửa Service.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    /// <summary>Email lấy từ token (null nếu token không có claim email).</summary>
    string? Email { get; }

    string Role { get; }
    bool IsAuthenticated { get; }
}
