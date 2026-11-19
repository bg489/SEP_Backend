using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Mappings;

public static class AuthMappings
{
    public static CurrentUserResponse ToResponse(this AppUser user)
        => new(user.Id, user.Email, user.DisplayName, user.Role, user.IsActive);
}
