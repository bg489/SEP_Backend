namespace AutoScenarioX.Api.Models.Entities;

public class Tag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ScenarioTag> ScenarioTags { get; set; } = new List<ScenarioTag>();
}

/// <summary>asx.scenario_tags - bảng nối nhiều-nhiều Scenario - Tag.</summary>
public class ScenarioTag
{
    public Guid ScenarioId { get; set; }
    public Guid TagId { get; set; }

    public Scenario Scenario { get; set; } = default!;
    public Tag Tag { get; set; } = default!;
}
