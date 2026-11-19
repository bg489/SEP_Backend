namespace AutoScenarioX.Api.Common.Options;

/// <summary>Section "DevAuth" trong appsettings.Development.json.</summary>
public sealed class DevAuthOptions
{
    public const string SectionName = "DevAuth";
    public Guid UserId { get; set; }
    public string Role { get; set; } = "SCENARIO_ENGINEER";
}
