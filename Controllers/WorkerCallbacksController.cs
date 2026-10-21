using System.Security.Cryptography;
using System.Text;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using AutoScenarioX.AuthApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/worker-callbacks/runs")]
public sealed class WorkerCallbacksController(
    IConfiguration configuration,
    ScenarioRunCallbackWriter callbackWriter,
    AuditLogWriter auditLog) : ControllerBase
{
    [HttpPost("{id}")]
    public async Task<IActionResult> Callback(string id, WorkerRunCallbackRequest callback, CancellationToken cancellationToken)
    {
        var expectedKey = configuration["Worker:CallbackKey"];
        if (string.IsNullOrWhiteSpace(expectedKey) || Encoding.UTF8.GetByteCount(expectedKey) < 32)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Worker callbacks are not configured on this API." });
        var suppliedKey = Request.Headers["X-Worker-Key"].ToString();
        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        if (expectedBytes.Length != suppliedBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
            return Unauthorized();
        try
        {
            var updated = await callbackWriter.ApplyAsync(id, callback, cancellationToken);
            if (updated is null) return NotFound(new { message = "Run not found." });
            await auditLog.WriteAsync(callback.WorkerId ?? "CARLA worker", $"Worker callback: {updated.State}", id, "Success", cancellationToken);
            return Ok(updated);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
