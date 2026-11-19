namespace AutoScenarioX.Api.Models.Entities;

public sealed class SimulationWorker
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "localhost";
    public int RpcPort { get; set; } = 2000;
    public int Capacity { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public string Quality { get; set; } = "Low";
    public string CarlaVersion { get; set; } = "0.9.16";
    public string MapsJson { get; set; } = "[]";
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
