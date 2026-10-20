using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/audit-log")]
public sealed class AuditLogController(WorkflowDocumentRepository repository) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<IReadOnlyList<AuditEventResponse>>> List(CancellationToken cancellationToken) =>
        Ok((await repository.ListAsync<AuditEventResponse>("audit", cancellationToken)).Take(500).ToList());
}
