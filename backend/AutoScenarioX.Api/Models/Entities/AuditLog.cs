namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.audit_logs - nhật ký thao tác (traceability).</summary>
public class AuditLog
{
    public long Id { get; set; }                             // identity
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = default!;
    public Guid ActorId { get; set; }
    public string Details { get; set; } = "{}";              // jsonb
    public DateTimeOffset CreatedAt { get; set; }
}
