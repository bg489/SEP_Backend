using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using AutoScenarioX.AuthApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = RoleNames.Administrator)]
public sealed class AdminUsersController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    AuthDbContext database) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<AuthUserResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AuthUserResponse>>> List(CancellationToken cancellationToken)
    {
        var users = await userManager.Users
            .OrderBy(user => user.Email)
            .Take(250)
            .ToListAsync(cancellationToken);
        var response = new List<AuthUserResponse>(users.Count);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var role = RoleNames.All.FirstOrDefault(roles.Contains) ?? string.Empty;
            response.Add(AuthUserResponse.From(user, role));
        }

        return Ok(response);
    }

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType<AuthUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthUserResponse>> SetRole(
        Guid id,
        ChangeRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!RoleNames.All.Contains(request.Role, StringComparer.Ordinal))
        {
            return BadRequest(new MessageResponse("Choose one of the four supported AutoScenarioX roles."));
        }

        if (!await roleManager.RoleExistsAsync(request.Role))
        {
            return BadRequest(new MessageResponse("The requested role has not been initialized."));
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(new MessageResponse("User not found."));

        var currentUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (Guid.TryParse(currentUserId, out var signedInUserId) &&
            signedInUserId == id && request.Role != RoleNames.Administrator)
        {
            return BadRequest(new MessageResponse("You cannot remove your own administrator role."));
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Contains(RoleNames.Administrator, StringComparer.Ordinal) &&
            request.Role != RoleNames.Administrator)
        {
            var administrators = await userManager.GetUsersInRoleAsync(RoleNames.Administrator);
            if (administrators.Count <= 1)
            {
                return BadRequest(new MessageResponse("The last administrator cannot be demoted."));
            }
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var removal = await userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removal.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new MessageResponse(
                string.Join(" ", removal.Errors.Select(error => error.Description))));
        }

        var assignment = await userManager.AddToRoleAsync(user, request.Role);
        if (!assignment.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new MessageResponse(
                string.Join(" ", assignment.Errors.Select(error => error.Description))));
        }

        await transaction.CommitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(AuthUserResponse.From(user, request.Role));
    }
}
