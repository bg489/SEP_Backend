namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.app_users - người dùng hệ thống (bản tối thiểu; đầy đủ thuộc Flow Admin).</summary>
public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string Role { get; set; } = default!;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
