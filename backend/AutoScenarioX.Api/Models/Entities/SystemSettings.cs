namespace AutoScenarioX.Api.Models.Entities;

/// <summary>Single persisted administration profile. Retention is a policy, not an automatic deletion command.</summary>
public sealed class SystemSettings
{
    public int Id { get; set; } = 1;
    public Guid? ActiveOracleConfigId { get; set; }
    public decimal SimilarityWarningPercent { get; set; } = 85;
    public decimal SimilarityBlockPercent { get; set; } = 98;
    public int RetentionDays { get; set; } = 90;
    public bool RetentionEnabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Immutable, reproducible comparison output; inputs are immutable scenario version IDs.</summary>
public sealed class SimilarityAnalysis
{
    public Guid Id { get; set; }
    public string Method { get; set; } = "gower";
    public string ResultJson { get; set; } = "{}";
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
