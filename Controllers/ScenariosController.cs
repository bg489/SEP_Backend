using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using AutoScenarioX.AuthApi.Auth;
using AutoScenarioX.AuthApi.Contracts;
using AutoScenarioX.AuthApi.Data;
using AutoScenarioX.AuthApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoScenarioX.AuthApi.Controllers;

[ApiController]
[Authorize]
[Route("api/scenarios")]
public sealed class ScenariosController(WorkflowDocumentRepository repository, AuditLogWriter auditLog) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Statuses = ["Draft", "Active", "Archived"];
    private static readonly HashSet<string> VehicleBlueprints = new(StringComparer.Ordinal)
    {
        "vehicle.audi.a2", "vehicle.audi.etron", "vehicle.audi.tt", "vehicle.bmw.grandtourer",
        "vehicle.chevrolet.impala", "vehicle.citroen.c3", "vehicle.dodge.charger_police", "vehicle.ford.mustang",
        "vehicle.jeep.wrangler_rubicon", "vehicle.lincoln.mkz_2017", "vehicle.mercedes.coupe", "vehicle.micro.microlino",
        "vehicle.mini.cooper_s", "vehicle.nissan.micra", "vehicle.nissan.patrol", "vehicle.seat.leon",
        "vehicle.tesla.model3", "vehicle.toyota.prius", "vehicle.dodge.charger_2020", "vehicle.dodge.charger_police_2020",
        "vehicle.ford.crown", "vehicle.lincoln.mkz_2020", "vehicle.mercedes.coupe_2020", "vehicle.mini.cooper_s_2021",
        "vehicle.nissan.patrol_2021", "vehicle.carlamotors.carlacola", "vehicle.carlamotors.european_hgv",
        "vehicle.carlamotors.firetruck", "vehicle.tesla.cybertruck", "vehicle.volkswagen.t2", "vehicle.ford.ambulance",
        "vehicle.mercedes.sprinter", "vehicle.volkswagen.t2_2021", "vehicle.mitsubishi.fusorosa",
        "vehicle.harley-davidson.low_rider", "vehicle.kawasaki.ninja", "vehicle.vespa.zx125", "vehicle.yamaha.yzf",
        "vehicle.bh.crossbike", "vehicle.diamondback.century", "vehicle.gazelle.omafiets"
    };

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
        if (request.ExecutionMode != "CARLA actors")
            return BadRequest(new MessageResponse("New scenarios must use CARLA actor execution. Import an .xosc/.osc file to run an OpenSCENARIO source."));
        var scenario = new ScenarioResponse(
            NewId("SCN"), request.Name.Trim(), request.Description?.Trim() ?? string.Empty,
            request.Map.Trim(), request.Category.Trim(), request.Severity.Trim(), "Draft", "v1.0",
            request.Weather.Trim(), 1 + Math.Clamp(request.TrafficVehicleCount, 0, 50) + Math.Clamp(request.PedestrianCount, 0, 100) + Math.Clamp(request.ObstacleCount, 0, 100), 0, 0, "Never", ActorName(),
            [request.Category.Trim().ToLowerInvariant(), request.Weather.Trim().ToLowerInvariant().Replace(' ', '-')],
            PriorityFor(request.Severity), request.RouteDescription.Trim(), request.EgoVehicle.Trim(), request.TrafficDensity.Trim(),
            request.PedestrianCount, request.ObstacleCount, request.TriggerCondition.Trim(), request.Actions.Trim(), null,
            request.ExecutionMode.Trim(), request.TrafficVehicle.Trim(), request.TrafficVehicleCount,
            request.PedestrianBlueprint.Trim(), request.ObstacleBlueprint.Trim(), request.ExecutionDurationSeconds, request.SpeedLimitOffsetPercent);
        var issues = ValidateConfiguration(scenario);
        if (issues.Count > 0) return BadRequest(new MessageResponse("Scenario configuration is invalid: " + string.Join("; ", issues)));
        await SaveScenarioAsync(scenario, "Scenario created", cancellationToken);
        if (scenario.ExecutionMode == "CARLA actors")
            await repository.SaveAsync("scenario-artifact", ArtifactKey(scenario.Id, scenario.Version), CreateNativeArtifact(scenario), cancellationToken);
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
        var sourceIssue = OpenScenarioSource.Validate(request.SourceContent);
        if (sourceIssue is not null) return BadRequest(new MessageResponse(sourceIssue));

        var scenario = request.Scenario with
        {
            Id = NewId("SCN"),
            Status = "Draft",
            Version = "v1.0",
            RunCount = 0,
            PassRate = 0,
            LastRun = "Never",
            Owner = ActorName(),
            SourceFileName = Path.GetFileName(request.SourceFileName),
            Actors = XDocument.Parse(request.SourceContent).Descendants().Count(element => element.Name.LocalName == "ScenarioObject"),
            ExecutionMode = "OpenSCENARIO source",
            RouteDescription = request.Scenario.RouteDescription ?? "Imported OpenSCENARIO route",
            EgoVehicle = request.Scenario.EgoVehicle ?? "vehicle.tesla.model3",
            TrafficDensity = request.Scenario.TrafficDensity ?? "Medium",
            PedestrianCount = request.Scenario.PedestrianCount ?? 0,
            ObstacleCount = request.Scenario.ObstacleCount ?? 0,
            TriggerCondition = request.Scenario.TriggerCondition ?? "Defined by imported OpenSCENARIO",
            Actions = request.Scenario.Actions ?? "Defined by imported OpenSCENARIO",
            TrafficVehicle = request.Scenario.TrafficVehicle ?? "vehicle.lincoln.mkz_2020",
            TrafficVehicleCount = request.Scenario.TrafficVehicleCount ?? 0,
            PedestrianBlueprint = request.Scenario.PedestrianBlueprint ?? "walker.pedestrian.0001",
            ObstacleBlueprint = request.Scenario.ObstacleBlueprint ?? "static.prop.streetbarrier",
            ExecutionDurationSeconds = request.Scenario.ExecutionDurationSeconds ?? 30,
            SpeedLimitOffsetPercent = request.Scenario.SpeedLimitOffsetPercent ?? 0
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
            Owner = current.Owner,
            ExecutionMode = json["executionMode"]?.GetValue<string>() ?? current.ExecutionMode ??
                (current.SourceFileName is null ? "CARLA actors" : "OpenSCENARIO source"),
            EgoVehicle = json["egoVehicle"]?.GetValue<string>() ?? current.EgoVehicle ?? "vehicle.tesla.model3",
            TrafficDensity = json["trafficDensity"]?.GetValue<string>() ?? current.TrafficDensity ?? "Medium",
            TrafficVehicle = json["trafficVehicle"]?.GetValue<string>() ?? current.TrafficVehicle ?? "vehicle.lincoln.mkz_2020",
            TrafficVehicleCount = json["trafficVehicleCount"]?.GetValue<int>() ?? current.TrafficVehicleCount ?? 0,
            PedestrianBlueprint = json["pedestrianBlueprint"]?.GetValue<string>() ?? current.PedestrianBlueprint ?? "walker.pedestrian.0001",
            ObstacleBlueprint = json["obstacleBlueprint"]?.GetValue<string>() ?? current.ObstacleBlueprint ?? "static.prop.streetbarrier",
            ExecutionDurationSeconds = json["executionDurationSeconds"]?.GetValue<int>() ?? current.ExecutionDurationSeconds ?? 30,
            SpeedLimitOffsetPercent = json["speedLimitOffsetPercent"]?.GetValue<int>() ?? current.SpeedLimitOffsetPercent ?? 0,
            RouteDescription = json["routeDescription"]?.GetValue<string>() ?? current.RouteDescription ?? "Start → route → goal",
            TriggerCondition = json["triggerCondition"]?.GetValue<string>() ?? current.TriggerCondition ?? "Simulation starts",
            Actions = json["actions"]?.GetValue<string>() ?? current.Actions ?? "Autopilot follows map traffic rules",
            Actors = 1 + Math.Clamp(json["trafficVehicleCount"]?.GetValue<int>() ?? current.TrafficVehicleCount ?? 0, 0, 50) +
                Math.Clamp(json["pedestrianCount"]?.GetValue<int>() ?? current.PedestrianCount ?? 0, 0, 100) +
                Math.Clamp(json["obstacleCount"]?.GetValue<int>() ?? current.ObstacleCount ?? 0, 0, 100)
        };
        var issues = ValidateConfiguration(next);
        if (issues.Count > 0) return BadRequest(new MessageResponse("Scenario configuration is invalid: " + string.Join("; ", issues)));
        if (next.ExecutionMode == "OpenSCENARIO source" &&
            (string.IsNullOrWhiteSpace(current.SourceFileName) ||
             !current.SourceFileName.EndsWith(".xosc", StringComparison.OrdinalIgnoreCase) &&
             !current.SourceFileName.EndsWith(".osc", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new MessageResponse("This scenario has no imported OpenSCENARIO source. Use the CARLA actor scene format or import a .xosc/.osc file."));
        if (next.ExecutionMode == "CARLA actors") next = next with { SourceFileName = null };
        else next = next with { Actors = current.Actors };
        await SaveScenarioAsync(next, "Edited scenario definition", cancellationToken);
        if (next.ExecutionMode == "CARLA actors")
            await repository.SaveAsync("scenario-artifact", ArtifactKey(id, next.Version), CreateNativeArtifact(next), cancellationToken);
        else
        {
            var source = await repository.GetAsync<ScenarioArtifactResponse>("scenario-artifact", ArtifactKey(id, current.Version), cancellationToken);
            if (source is not null)
                await repository.SaveAsync("scenario-artifact", ArtifactKey(id, next.Version), source with { Version = next.Version }, cancellationToken);
        }
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
        if (copy.ExecutionMode == "CARLA actors")
            await repository.SaveAsync("scenario-artifact", ArtifactKey(copy.Id, copy.Version), CreateNativeArtifact(copy), cancellationToken);
        else if (source is not null)
            await repository.SaveAsync("scenario-artifact", ArtifactKey(copy.Id, copy.Version), source with { ScenarioId = copy.Id, Version = copy.Version }, cancellationToken);
        await auditLog.WriteAsync(User, "Clone scenario", $"{id} → {copy.Id}", "Success", cancellationToken);
        return CreatedAtAction(nameof(List), new { id = copy.Id }, copy);
    }

    [HttpPut("{id}/status")]
    [Authorize(Roles = RoleNames.ScenarioEngineer + "," + RoleNames.Administrator)]
    public async Task<ActionResult<ScenarioResponse>> SetStatus(string id, ScenarioStatusRequest request, CancellationToken cancellationToken)
    {
        if (!Statuses.Contains(request.Status)) return BadRequest(new MessageResponse("Unsupported scenario status."));
        var current = await repository.GetAsync<ScenarioResponse>("scenario", id, cancellationToken);
        if (current is null) return NotFound(new MessageResponse("Scenario not found."));
        if (request.Status == "Active")
        {
            var issues = ValidateConfiguration(current);
            var artifact = await repository.GetAsync<ScenarioArtifactResponse>("scenario-artifact", ArtifactKey(id, current.Version), cancellationToken);
            if (artifact is null || current.ExecutionMode == "CARLA actors" && !artifact.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                issues.Add("a saved execution definition is required for this version");
            if (current.ExecutionMode == "OpenSCENARIO source" && artifact is not null &&
                !artifact.FileName.EndsWith(".xosc", StringComparison.OrdinalIgnoreCase) &&
                !artifact.FileName.EndsWith(".osc", StringComparison.OrdinalIgnoreCase))
                issues.Add("the imported OpenSCENARIO source file is missing");
            else if (current.ExecutionMode == "OpenSCENARIO source" && artifact is not null &&
                OpenScenarioSource.Validate(artifact.Content, requireCriteria: true) is { } sourceIssue)
                issues.Add(sourceIssue);
            if (issues.Count > 0)
                return BadRequest(new MessageResponse("Scenario configuration is not ready for testing: " + string.Join("; ", issues)));
        }
        var updated = current with { Status = request.Status };
        await repository.SaveAsync("scenario", id, updated, cancellationToken);
        await AppendVersionAsync(updated, string.IsNullOrWhiteSpace(request.Note) ? $"Status changed to {request.Status}" : request.Note, cancellationToken);
        var action = request.Status == "Active" ? "System validated scenario configuration" : $"Scenario status: {request.Status}";
        await auditLog.WriteAsync(User, action, id, "Success", cancellationToken);
        return Ok(updated);
    }

    private static List<string> ValidateConfiguration(ScenarioResponse scenario)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(scenario.Name)) issues.Add("scenario name is required");
        if (scenario.Map is not ("Town01" or "Town02" or "Town03" or "Town04" or "Town05" or "Town06" or "Town07" or "Town10HD"))
            issues.Add("a supported CARLA map is required");
        if (scenario.Category is not ("Urban" or "Highway" or "Intersection" or "Rural" or "Parking"))
            issues.Add("a supported scenario category is required");
        if (scenario.Weather is not ("Clear noon" or "Clear morning" or "Sunset" or "Overcast" or "Light rain" or "Heavy rain" or "Fog"))
            issues.Add("a supported weather preset is required");
        if (scenario.ExecutionMode == "OpenSCENARIO source") return issues;
        if (string.IsNullOrWhiteSpace(scenario.RouteDescription)) issues.Add("route description is required");
        if (scenario.EgoVehicle is null || !VehicleBlueprints.Contains(scenario.EgoVehicle))
            issues.Add("a supported CARLA ego vehicle blueprint is required");
        if (scenario.TrafficDensity is not ("Low" or "Medium" or "High")) issues.Add("traffic density is required");
        if (scenario.PedestrianCount is not (>= 0 and <= 100)) issues.Add("pedestrian count must be between 0 and 100");
        if (scenario.ObstacleCount is not (>= 0 and <= 100)) issues.Add("obstacle count must be between 0 and 100");
        if (scenario.TrafficVehicle is null || !VehicleBlueprints.Contains(scenario.TrafficVehicle))
            issues.Add("a supported CARLA traffic vehicle blueprint is required");
        if (scenario.TrafficVehicleCount is not (>= 0 and <= 50)) issues.Add("traffic vehicle count must be between 0 and 50");
        if (scenario.PedestrianBlueprint is null || !System.Text.RegularExpressions.Regex.IsMatch(scenario.PedestrianBlueprint, "^walker\\.pedestrian\\.\\d{4}$"))
            issues.Add("a valid CARLA pedestrian blueprint is required");
        if (scenario.ObstacleBlueprint is not ("static.prop.streetbarrier" or "static.prop.trafficcone01" or "static.prop.warningconstruction" or "static.prop.trafficwarning"))
            issues.Add("a supported obstacle blueprint is required");
        if (scenario.ExecutionDurationSeconds is not (>= 5 and <= 600)) issues.Add("execution duration must be between 5 and 600 seconds");
        if (scenario.SpeedLimitOffsetPercent is not (>= -30 and <= 70)) issues.Add("speed offset must be between -30 and 70 percent");
        if (scenario.ExecutionMode is not ("CARLA actors" or "OpenSCENARIO source")) issues.Add("a supported execution mode is required");
        if (string.IsNullOrWhiteSpace(scenario.TriggerCondition)) issues.Add("trigger condition is required");
        if (string.IsNullOrWhiteSpace(scenario.Actions)) issues.Add("scenario action is required");
        return issues;
    }

    private static ScenarioArtifactResponse CreateNativeArtifact(ScenarioResponse scenario)
    {
        var content = JsonSerializer.Serialize(scenario, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(content);
        return new ScenarioArtifactResponse(scenario.Id, scenario.Version, $"{scenario.Id}_{scenario.Version}.json", content,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
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
