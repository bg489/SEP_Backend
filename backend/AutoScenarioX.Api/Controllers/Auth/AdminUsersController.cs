using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.DTOs.Auth;
using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.Api.Controllers.Auth;

[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Roles = AppRoles.Admin)]
public sealed class AdminUsersController(IAdminUsersService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CurrentUserResponse>>> Search([FromQuery] string? search, [FromQuery] string? role,
        [FromQuery] bool? isActive, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await service.SearchAsync(search, role, isActive, page, pageSize, ct));

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<CurrentUserResponse>> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
}
