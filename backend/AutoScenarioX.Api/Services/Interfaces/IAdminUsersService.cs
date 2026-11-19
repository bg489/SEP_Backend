using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.DTOs.Common;

namespace AutoScenarioX.Api.Services.Interfaces;

public interface IAdminUsersService
{
    Task<PagedResult<CurrentUserResponse>> SearchAsync(string? search, string? role, bool? isActive, int page, int pageSize, CancellationToken ct = default);
    Task<CurrentUserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
}
