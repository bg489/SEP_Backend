using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/scenarios")]
public sealed class ScenariosController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Statuses = ["Draft", "In review", "Active", "Approved", "Archived"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScenarioResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var scenarios = await repository.ListAsync<ScenarioResponse>("scenario", cancellationToken);
        var query = search?.Trim();
        if (!string.IsNullOrEmpty(query))
            scenarios = scenarios.Where(item => string.Join(' ', item.Id, item.Name, item.Description, item.Map, item.Category, string.Join(' ', item.Tags))
                .Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(status) && status != "All statuses")
            scenarios = scenarios.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        return Ok(scenarios);
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.ScenarioEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<ScenarioResponse>> Create(CreateScenarioRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Map))
            return BadRequest(new MessageResponse("Scenario name and map are required."));
        var now = DateTimeOffset.UtcNow;
        var scenario = new ScenarioResponse(
            NewId("SCN"), request.Name.Trim(), request.Description?.Trim() ?? string.Empty,
            request.Map.Trim(), request.Category.Trim(), request.Severity.Trim(), "Draft", "v1.0",
            request.Weather.Trim(), 2, 0, 0, "Never", ActorName(),
            [request.Category.Trim().ToLowerInvariant(), request.Weather.Trim().ToLowerInvariant().Replace(' ', '-')],
            PriorityFor(request.Severity));
        await SaveScenarioAsync(scenario, "Scenario created", cancellationToken);
        await auditLog.WriteAsync(User, "Create scenario", scenario.Id, "Success", cancellationToken);
        return CreatedAtAction(nameof(List), new { id = scenario.Id }, scenario);
    }

    [HttpPost("import")]
    [Authorize(Roles = RoleNames.ScenarioEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<ScenarioResponse>> Import(ScenarioImportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFileName) ||
            !request.SourceFileName.EndsWith(".xosc", StringComparison.OrdinalIgnoreCase) &&
            !request.SourceFileName.EndsWith(".osc", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new MessageResponse("Only .xosc and .osc files can be imported."));
        if (string.IsNullOrWhiteSpace(request.SourceContent) || Encoding.UTF8.GetByteCount(request.SourceContent) > 10 * 1024 * 1024)
            return BadRequest(new MessageResponse("Scenario files must contain valid XML and be no larger than 10 MB."));
        try
        {
            using var input = new StringReader(request.SourceContent);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader);
            if (document.Root?.Name.LocalName != "OpenSCENARIO" ||
                !document.Descendants().Any(element => element.Name.LocalName == "Storyboard"))
                return BadRequest(new MessageResponse("The file must be OpenSCENARIO XML containing a Storyboard."));
        }
        catch (XmlException)
        {
            return BadRequest(new MessageResponse("The selected scenario file is not valid XML."));
        }

        var scenario = request.Scenario with
        {
            Id = NewId("SCN"),
            Status = "Draft",
            Version = "v1.0",
            RunCount = 0,
            PassRate = 0,
            LastRun = "Never",
            Owner = ActorName(),
            SourceFileName = Path.GetFileName(request.SourceFileName)
        };
        var sourceBytes = Encoding.UTF8.GetBytes(request.SourceContent);
        var artifact = new ScenarioArtifactResponse(
            scenario.Id, scenario.Version, scenario.SourceFileName!, request.SourceContent,
            Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant());
        await SaveScenarioAsync(scenario, $"Imported {scenario.SourceFileName ?? "scenario definition"}", cancellationToken);
        await repository.SaveAsync("scenario-artifact", ArtifactKey(scenario.Id, scenario.Version), artifact, cancellationToken);
        await auditLog.WriteAsync(User, "Import scenario", scenario.Id, "Success", cancellationToken);
        return CreatedAtAction(nameof(List), new { id = scenario.Id }, scenario);
    }

    [HttpPost("{id}/versions")]
    [Authorize(Roles = RoleNames.ScenarioEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<ScenarioResponse>> SaveVersion(string id, [FromBody] JsonObject changes, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync<ScenarioResponse>("scenario", id, cancellationToken);
        if (current is null) return NotFound(new MessageResponse("Scenario not found."));
        var json = JsonSerializer.SerializeToNode(current, JsonOptions)!.AsObject();
        foreach (var change in changes)
        {
            if (change.Key is "id" or "version" or "runCount" or "passRate" or "lastRun" or "owner") continue;
            json[change.Key] = change.Value?.DeepClone();
        }
        var next = json.Deserialize<ScenarioResponse>(JsonOptions)! with
        {
            Id = id,
            Version = NextVersion(current.Version),
            Status = "Draft",
            Owner = current.Owner
        };
        await SaveScenarioAsync(next, "Edited scenario definition", cancellationToken);
        var source = await repository.GetAsync<ScenarioArtifactResponse>("scenario-artifact", ArtifactKey(id, current.Version), cancellationToken);
        if (source is not null)
            await repository.SaveAsync("scenario-artifact", ArtifactKey(id, next.Version), source with { Version = next.Version }, cancellationToken);
        await auditLog.WriteAsync(User, "Create scenario version", $"{id} {next.Version}", "Success", cancellationToken);
        return Ok(next);
    }

    [HttpPost("{id}/clone")]
    [Authorize(Roles = RoleNames.ScenarioEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<ScenarioResponse>> Clone(string id, CancellationToken cancellationToken)
    {
        var original = await repository.GetAsync<ScenarioResponse>("scenario", id, cancellationToken);
        if (original is null) return NotFound(new MessageResponse("Scenario not found."));
        var copy = original with
        {
            Id = NewId("SCN"),
            Name = $"Copy of {original.Name}",
            Version = "v1.0",
            Status = "Draft",
            RunCount = 0,
            PassRate = 0,
            LastRun = "Never",
            Owner = ActorName(),
            Tags = [.. original.Tags, "clone"]
        };
        await SaveScenarioAsync(copy, $"Cloned from {id}", cancellationToken);
        var source = await repository.GetAsync<ScenarioArtifactResponse>("scenario-artifact", ArtifactKey(id, original.Version), cancellationToken);
        if (source is not null)
            await repository.SaveAsync("scenario-artifact", ArtifactKey(copy.Id, copy.Version), source with { ScenarioId = copy.Id }, cancellationToken);
        await auditLog.WriteAsync(User, "Clone scenario", $"{id} → {copy.Id}", "Success", cancellationToken);
        return CreatedAtAction(nameof(List), new { id = copy.Id }, copy);
    }

    [HttpPut("{id}/status")]
    public async Task<ActionResult<ScenarioResponse>> SetStatus(string id, ScenarioStatusRequest request, CancellationToken cancellationToken)
    {
        if (!Statuses.Contains(request.Status)) return BadRequest(new MessageResponse("Unsupported scenario status."));
        if (request.Status == "Active" && !User.IsInRole(RoleNames.TestReviewer) && !User.IsInRole(RoleNames.Administrator))
            return Forbid();
        if (request.Status != "Active" && !User.IsInRole(RoleNames.ScenarioEngineer) && !User.IsInRole(RoleNames.TestReviewer) && !User.IsInRole(RoleNames.Administrator))
            return Forbid();
        var current = await repository.GetAsync<ScenarioResponse>("scenario", id, cancellationToken);
        if (current is null) return NotFound(new MessageResponse("Scenario not found."));
        var updated = current with { Status = request.Status };
        await repository.SaveAsync("scenario", id, updated, cancellationToken);
        await AppendVersionAsync(updated, string.IsNullOrWhiteSpace(request.Note) ? $"Status changed to {request.Status}" : request.Note, cancellationToken);
        await auditLog.WriteAsync(User, $"Scenario status: {request.Status}", id, "Success", cancellationToken);
        return Ok(updated);
    }

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<IReadOnlyList<ScenarioVersionResponse>>> Versions(string id, CancellationToken cancellationToken)
    {
        var scenario = await repository.GetAsync<ScenarioResponse>("scenario", id, cancellationToken);
        if (scenario is null) return NotFound(new MessageResponse("Scenario not found."));
        var versions = await repository.GetAsync<List<ScenarioVersionResponse>>("scenario-versions", id, cancellationToken)
            ?? [VersionRecord(scenario, "Initial scenario definition")];
        return Ok(versions);
    }

    [HttpPatch("{id}/execution-metrics")]
    [Authorize(Roles = RoleNames.TestEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<ScenarioResponse>> UpdateMetrics(string id, ExecutionMetricsRequest request, CancellationToken cancellationToken)
    {
        if (request.RunCount < 0 || request.PassRate is < 0 or > 100)
            return BadRequest(new MessageResponse("Run count and pass rate are outside their valid range."));
        var current = await repository.GetAsync<ScenarioResponse>("scenario", id, cancellationToken);
        if (current is null) return NotFound(new MessageResponse("Scenario not found."));
        var updated = current with { RunCount = request.RunCount, PassRate = request.PassRate, LastRun = request.LastRun };
        await repository.SaveAsync("scenario", id, updated, cancellationToken);
        await auditLog.WriteAsync(User, "Update scenario execution metrics", id, "Success", cancellationToken);
        return Ok(updated);
    }

    private async Task SaveScenarioAsync(ScenarioResponse scenario, string note, CancellationToken cancellationToken)
    {
        await repository.SaveAsync("scenario", scenario.Id, scenario, cancellationToken);
        await AppendVersionAsync(scenario, note, cancellationToken);
    }

    private async Task AppendVersionAsync(ScenarioResponse scenario, string note, CancellationToken cancellationToken)
    {
        var versions = await repository.GetAsync<List<ScenarioVersionResponse>>("scenario-versions", scenario.Id, cancellationToken) ?? [];
        versions.RemoveAll(item => item.Version == scenario.Version);
        versions.Insert(0, VersionRecord(scenario, note));
        await repository.SaveAsync("scenario-versions", scenario.Id, versions, cancellationToken);
    }

    private static ScenarioVersionResponse VersionRecord(ScenarioResponse scenario, string note)
    {
        var json = JsonSerializer.Serialize(scenario, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()[..16];
        return new ScenarioVersionResponse(scenario.Version, DateTimeOffset.UtcNow.ToString("O"), note, hash, scenario.Status);
    }

    private string ActorName() => User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "AutoScenarioX user";
    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
    private static string ArtifactKey(string id, string version) => $"{id}:{version}";
    private static int PriorityFor(string severity) => severity switch { "Critical" => 92, "High" => 80, "Medium" => 62, _ => 45 };
    private static string NextVersion(string current)
    {
        var parts = current.TrimStart('v').Split('.');
        return parts.Length == 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor)
            ? $"v{major}.{minor + 1}"
            : "v1.1";
    }
}
