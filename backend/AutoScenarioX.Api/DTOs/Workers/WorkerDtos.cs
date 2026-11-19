namespace AutoScenarioX.Api.DTOs.Workers;

public sealed record RegisterWorkerRequest(string Name, string Host, int RpcPort = 2000, int Capacity = 1, string CarlaVersion = "0.9.16", IReadOnlyList<string>? Maps = null);
public sealed record WorkerHeartbeatRequest(IReadOnlyList<string>? Maps = null, string? Error = null);
public sealed record UpdateWorkerRequest(bool Enabled, int Capacity);
public sealed record WorkerResponse(Guid Id, string Name, string Host, int RpcPort, int Capacity, int ActiveRuns,
    int AvailableCapacity, bool Enabled, string Health, string Quality, string CarlaVersion, IReadOnlyList<string> Maps,
    DateTimeOffset? LastHeartbeatAt, string? LastError);
