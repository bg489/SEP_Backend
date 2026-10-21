using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using AutoScenarioX.AuthApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/integration/carla")]
public sealed class CarlaIntegrationController(WorkflowDocumentRepository repository, AuditLogWriter auditLog, CarlaRuntime carlaRuntime) : ControllerBase
{
    private static readonly CarlaIntegrationResponse Default = new(
        "127.0.0.1", 2000, 2001, "0.9.16", "C:\\Users\\admin\\Downloads\\CARLA_0.9.16\\ScenarioRunner", "Low", false, false, "Not tested", "Not configured");

    [HttpGet]
    public async Task<ActionResult<CarlaIntegrationResponse>> Get(CancellationToken cancellationToken)
    {
        var config = await repository.GetAsync<CarlaIntegrationResponse>("integration", "carla", cancellationToken) ?? Default;
        return Ok(config with { ScenarioRunnerPath = carlaRuntime.ScenarioRunnerRoot(config), QualityLevel = "Low" });
    }

    [HttpPut]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<CarlaIntegrationResponse>> Save(CarlaIntegrationResponse config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.ScenarioRunnerPath))
            return BadRequest(new MessageResponse("CARLA host and ScenarioRunner path are required."));
        if (config.RpcPort is < 1 or > 65535 || config.StreamingPort is < 1 or > 65535 || config.RpcPort == config.StreamingPort)
            return BadRequest(new MessageResponse("Use two different ports from 1 to 65535."));
        CarlaReadiness? readiness = null;
        if (config.Active)
        {
            readiness = await carlaRuntime.CheckReadinessAsync(config, cancellationToken);
            if (!readiness.Ready) return Conflict(new MessageResponse(readiness.Message));
        }
        var saved = config with
        {
            ScenarioRunnerPath = carlaRuntime.ScenarioRunnerRoot(config),
            QualityLevel = "Low",
            Validated = true,
            ConnectionStatus = readiness is null ? "Not tested" : readiness.Connected ? "Connected" : "Ready",
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        await repository.SaveAsync("integration", "carla", saved, cancellationToken);
        await auditLog.WriteAsync(User,
            saved.Active ? "Activate CARLA integration (Low quality)" : "Save CARLA integration settings (Low quality)",
            $"{saved.Host}:{saved.RpcPort}", "Success", cancellationToken);
        return Ok(saved);
    }

    [HttpPost("test")]
    [Authorize(Policy = RoleNames.Administrator)]
    public async Task<ActionResult<CarlaIntegrationResponse>> Test(CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync<CarlaIntegrationResponse>("integration", "carla", cancellationToken) ?? Default;
        var readiness = await carlaRuntime.CheckReadinessAsync(current, cancellationToken);
        var updated = current with
        {
            ScenarioRunnerPath = carlaRuntime.ScenarioRunnerRoot(current),
            Active = readiness.Ready && current.Active,
            Validated = readiness.Ready,
            ConnectionStatus = !readiness.Ready ? "Failed" : readiness.Connected ? "Connected" : "Ready",
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        await repository.SaveAsync("integration", "carla", updated, cancellationToken);
        await auditLog.WriteAsync(User, "Test CARLA and ScenarioRunner readiness", $"{current.Host}:{current.RpcPort}", readiness.Ready ? "Success" : "Rejected", cancellationToken);
        return readiness.Ready ? Ok(updated) : Conflict(new { message = readiness.Message, config = updated });
    }
}
