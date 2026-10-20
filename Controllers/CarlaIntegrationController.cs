using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/integration/carla")]
public sealed class CarlaIntegrationController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly CarlaIntegrationResponse Default = new(
        "127.0.0.1", 2000, 2001, "0.9.16", "C:\\Users\\admin\\Downloads\\CARLA_0.9.16", "Low", false, false, "Not tested", "Not configured");

    [HttpGet]
    public async Task<ActionResult<CarlaIntegrationResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await repository.GetAsync<CarlaIntegrationResponse>("integration", "carla", cancellationToken) ?? Default);

    [HttpPut]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<CarlaIntegrationResponse>> Save(CarlaIntegrationResponse config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.ScenarioRunnerPath))
            return BadRequest(new MessageResponse("CARLA host and ScenarioRunner path are required."));
        if (config.RpcPort is < 1 or > 65535 || config.StreamingPort is < 1 or > 65535 || config.RpcPort == config.StreamingPort)
            return BadRequest(new MessageResponse("Use two different ports from 1 to 65535."));
        if (config.ConnectionStatus is not ("Not tested" or "Connected (mock)" or "Failed (mock)"))
            return BadRequest(new MessageResponse("Unsupported CARLA connection status."));
        if (config.Active && config.ConnectionStatus != "Connected (mock)")
            return BadRequest(new MessageResponse("Pass the mock connection check before activation."));
        var saved = config with
        {
            QualityLevel = "Low",
            Validated = true,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        await repository.SaveAsync("integration", "carla", saved, cancellationToken);
        await auditLog.WriteAsync(User,
            saved.Active ? "Activate CARLA integration (Low quality)" : saved.ConnectionStatus == "Connected (mock)" ? "Test CARLA connection (mock only)" : "Save CARLA integration settings (Low quality)",
            $"{saved.Host}:{saved.RpcPort}", "Success", cancellationToken);
        return Ok(saved);
    }
}
