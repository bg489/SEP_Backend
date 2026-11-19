namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.scenarios - hồ sơ scenario (thay đổi được).</summary>
public class Scenario
{
    public Guid Id { get; set; }
    public long SeqNo { get; set; }                        // identity - DB sinh
    public string Code { get; set; } = default!;           // generated - SCN-00001
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Category { get; set; } = default!;
    public string Status { get; set; } = ScenarioStatuses.Active;
    public string OriginType { get; set; } = default!;
    public Guid? OriginVersionId { get; set; }
    public Guid? VariantBatchId { get; set; }
    public Guid? LatestVersionId { get; set; }
    public int LatestVersionNumber { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? ArchivedBy { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public ScenarioDraft? Draft { get; set; }
    public ICollection<ScenarioVersion> Versions { get; set; } = new List<ScenarioVersion>();
    public ICollection<ScenarioTag> ScenarioTags { get; set; } = new List<ScenarioTag>();
}

public static class ScenarioStatuses
{
    public const string Active = "ACTIVE";
    public const string Archived = "ARCHIVED";
}

/// <summary>Giá trị cho asx.scenarios.origin_type (khớp CHECK trong DB).</summary>
public static class ScenarioOriginTypes
{
    public const string Manual = "manual";
    public const string Import = "import";
    public const string Clone = "clone";
    public const string Variant = "variant";
}
