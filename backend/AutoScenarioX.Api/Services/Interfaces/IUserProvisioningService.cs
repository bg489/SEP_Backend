using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IUserProvisioningService
{
    /// <summary>
    /// Trả về dòng asx.app_users tương ứng user Supabase (id = claim "sub").
    /// Chưa có thì tạo mới nếu Auth:AutoProvisionUsers = true, ngược lại ném ForbiddenException.
    /// </summary>
    Task<AppUser> EnsureAsync(Guid userId, string? email, CancellationToken ct = default);
}
