using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/simulation-nodes")]
public sealed class SimulationNodesController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkerNodeResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await repository.ListAsync<WorkerNodeResponse>("worker", cancellationToken));

    [HttpPut]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<IReadOnlyList<WorkerNodeResponse>>> Save(List<WorkerNodeResponse> workers, CancellationToken cancellationToken)
    {
        if (workers.Any(worker => worker.Port is < 1 or > 65535 || worker.Capacity < 1 || worker.ActiveRuns < 0 || worker.ActiveRuns > worker.Capacity))
            return BadRequest(new MessageResponse("Each node needs a valid port, positive capacity and active-run count within capacity."));
        if (workers.Select(worker => worker.Id).Distinct(StringComparer.Ordinal).Count() != workers.Count)
            return BadRequest(new MessageResponse("Simulation node IDs must be unique."));
        await repository.ReplaceAsync("worker", workers, worker => worker.Id, cancellationToken);
        await auditLog.WriteAsync(User, "Update simulation nodes", $"{workers.Count} node(s)", "Success", cancellationToken);
        return Ok(workers);
    }
}
