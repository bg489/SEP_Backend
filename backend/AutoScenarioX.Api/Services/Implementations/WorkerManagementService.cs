using System.Text.Json;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Workers;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class WorkerManagementService(IWorkerRepository repository, ICurrentUser user) : IWorkerManagementService
{
    private static readonly HashSet<string> Maps = ["Town01", "Town01_Opt", "Town02", "Town02_Opt", "Town03", "Town03_Opt", "Town04", "Town04_Opt", "Town05", "Town05_Opt", "Town10HD", "Town10HD_Opt"];
    public async Task<IReadOnlyList<WorkerResponse>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<WorkerResponse>();
        foreach (var worker in await repository.ListAsync(ct)) result.Add(await Response(worker, ct));
        return result;
    }
    public async Task<WorkerResponse> RegisterAsync(RegisterWorkerRequest request, CancellationToken ct = default)
    {
        Validate(request.Capacity, request.Maps);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || string.IsNullOrWhiteSpace(request.Host)
            || request.Host.Length > 255 || request.RpcPort is < 1 or > 65535 || request.CarlaVersion != "0.9.16")
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Supply a name, host, valid RPC port and CARLA 0.9.16.");
        var existing = (await repository.ListAsync(ct)).FirstOrDefault(w => w.OwnerUserId == user.UserId && w.Name == request.Name.Trim());
        if (existing is not null) return await Response(existing, ct);
        var worker = new SimulationWorker
        {
            Id = Guid.NewGuid(), OwnerUserId = user.UserId, Name = request.Name.Trim(), Host = request.Host.Trim(),
            RpcPort = request.RpcPort, Capacity = request.Capacity, CarlaVersion = request.CarlaVersion,
            MapsJson = JsonSerializer.Serialize(request.Maps ?? []),
        };
        await repository.AddAsync(worker, ct);
        await repository.SaveAsync(ct);
        return await Response(worker, ct);
    }
    public async Task<WorkerResponse> HeartbeatAsync(Guid id, WorkerHeartbeatRequest request, CancellationToken ct = default)
    {
        var worker = await repository.GetAsync(id, ct) ?? throw Missing();
        if (worker.OwnerUserId != user.UserId && user.Role != AppRoles.Admin)
            throw new ForbiddenException(ErrorCodes.InvalidRequest, "Only the registering account can heartbeat this worker.");
        Validate(worker.Capacity, request.Maps);
        if (request.Error?.Length > 1000) throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Worker error must be at most 1000 characters.");
        // A restarted worker must not revive its expired lease before it is recovered.
        if (worker.LastHeartbeatAt < DateTimeOffset.UtcNow.AddSeconds(-90))
            await repository.RecoverLostAsync(ct);
        if (request.Maps is not null) worker.MapsJson = JsonSerializer.Serialize(request.Maps);
        worker.LastHeartbeatAt = DateTimeOffset.UtcNow;
        worker.LastError = string.IsNullOrWhiteSpace(request.Error) ? null : request.Error;
        worker.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveAsync(ct);
        return await Response(worker, ct);
    }
    public async Task<WorkerResponse> UpdateAsync(Guid id, UpdateWorkerRequest request, CancellationToken ct = default)
    {
        Validate(request.Capacity, null);
        var worker = await repository.GetAsync(id, ct) ?? throw Missing();
        worker.Enabled = request.Enabled; worker.Capacity = request.Capacity; worker.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveAsync(ct);
        return await Response(worker, ct);
    }
    public async Task RecoverLostAsync(CancellationToken ct = default) => await repository.RecoverLostAsync(ct);
    private async Task<WorkerResponse> Response(SimulationWorker worker, CancellationToken ct)
    {
        var active = await repository.ActiveRunsAsync(worker.Id, ct);
        var healthy = worker.Enabled && worker.LastError is null && worker.LastHeartbeatAt >= DateTimeOffset.UtcNow.AddSeconds(-90);
        return new(worker.Id, worker.Name, worker.Host, worker.RpcPort, worker.Capacity, active,
            healthy ? Math.Max(0, worker.Capacity - active) : 0, worker.Enabled,
            !worker.Enabled ? "Disabled" : healthy ? "Healthy" : worker.LastHeartbeatAt is null ? "Not tested" : "Offline",
            "Low", worker.CarlaVersion, JsonSerializer.Deserialize<string[]>(worker.MapsJson) ?? [], worker.LastHeartbeatAt, worker.LastError);
    }
    private static void Validate(int capacity, IReadOnlyList<string>? maps)
    {
        if (capacity is < 1 or > 16 || maps?.Any(map => !Maps.Contains(map)) == true)
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Capacity must be 1–16 and maps must be from the twelve CARLA maps.");
    }
    private static NotFoundException Missing() => new(ErrorCodes.InvalidRequest, "Simulation worker was not found.");
}
