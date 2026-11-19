using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/oracles/profile")]
public sealed class OracleProfileController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly OracleProfileResponse Default = new(1, true, true, "Default safety profile");

    [HttpGet]
    public async Task<ActionResult<OracleProfileResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await repository.GetAsync<OracleProfileResponse>("oracle-profile", "default", cancellationToken) ?? Default);

    [HttpPut]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<OracleProfileResponse>> Save(OracleProfileResponse profile, CancellationToken cancellationToken)
    {
        if (profile.Version < 1 || (profile.Active && !profile.Validated))
            return BadRequest(new MessageResponse("An active oracle profile must be validated and have a positive version."));

        await repository.SaveAsync("oracle-profile", "default", profile, cancellationToken);
        await auditLog.WriteAsync(User, "Save oracle profile state", $"Default safety profile v{profile.Version}", "Success", cancellationToken);
        return Ok(profile);
    }
}
