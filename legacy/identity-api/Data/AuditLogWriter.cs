using System.Security.Claims;
using AutoScenarioX.AuthApi.Contracts;

namespace AutoScenarioX.AuthApi.Data;

public sealed class AuditLogWriter(WorkflowDocumentRepository repository)
{
    public async Task WriteAsync(ClaimsPrincipal principal, string action, string target, string result, CancellationToken cancellationToken)
    {
        var actor = principal.Identity?.Name ?? principal.FindFirst("email")?.Value ?? "AutoScenarioX user";
        await WriteAsync(actor, action, target, result, cancellationToken);
    }

    public async Task WriteAsync(string actor, string action, string target, string result, CancellationToken cancellationToken)
    {
        var entry = new AuditEventResponse(
            $"AUD-{Guid.NewGuid():N}"[..12], DateTimeOffset.UtcNow.ToString("O"), actor, action, target, result);
        await repository.SaveAsync("audit", entry.Id, entry, cancellationToken);
    }
}
