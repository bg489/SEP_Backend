using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class AdminUsersService(IUnitOfWork uow, ICurrentUser currentUser) : IAdminUsersService
{
    private void RequireAdmin()
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedException(ErrorCodes.Unauthorized, "Sign in first.");
        if (currentUser.Role != AppRoles.Admin) throw new ForbiddenException(ErrorCodes.Forbidden, "Administrator access is required.");
    }

    public async Task<PagedResult<CurrentUserResponse>> SearchAsync(string? search, string? role, bool? isActive, int page, int pageSize, CancellationToken ct = default)
    {
        RequireAdmin();
        if (page is < 1 or > 100_000 || pageSize is < 1 or > 100) throw Invalid("page", "Page must be between 1 and 100000 and pageSize between 1 and 100.");
        role = string.IsNullOrWhiteSpace(role) ? null : role.Trim().ToUpperInvariant();
        if (role is not null && !AppRoles.All.Contains(role)) throw Invalid("role", "Unknown role.");
        var result = await uow.AppUsers.SearchAsync(search, role, isActive, page, pageSize, ct);
        return new PagedResult<CurrentUserResponse>(result.Items.Select(user => user.ToResponse()).ToList(), page, pageSize, result.Total);
    }

    public async Task<CurrentUserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        RequireAdmin();
        if (request.Role is null && request.IsActive is null) throw Invalid("user", "Provide a role or active status to change.");
        var role = request.Role?.Trim().ToUpperInvariant();
        if (role is not null && !AppRoles.All.Contains(role)) throw Invalid("role", "Unknown role.");
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var user = await uow.AppUsers.FindAsync([id], token)
                ?? throw new NotFoundException("USER_NOT_FOUND", "The account was not found.");
            if (user.Id == currentUser.UserId && ((role is not null && role != user.Role) || request.IsActive == false))
                throw new BusinessRuleException("ADMIN_SELF_CHANGE_FORBIDDEN", "An administrator cannot remove their own access.");
            var losesAdmin = user.IsActive && user.Role == AppRoles.Admin && (request.IsActive == false || (role is not null && role != AppRoles.Admin));
            if (losesAdmin && await uow.AppUsers.CountActiveAdminsAsync(token) <= 1)
                throw new BusinessRuleException("LAST_ADMIN_REQUIRED", "Keep at least one active administrator.");
            var before = new { user.Role, user.IsActive };
            if (role is not null) user.Role = role;
            if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            uow.AuditLogs.Write("USER", user.Id, "ADMIN_UPDATE_USER", currentUser.UserId, new { before, after = new { user.Role, user.IsActive } });
            await uow.SaveChangesAsync(token);
            return user.ToResponse();
        }, ct);
    }

    private static RequestValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
