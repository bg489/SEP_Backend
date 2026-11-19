namespace AutoScenarioX.Api.Common.Options;

/// <summary>Section "Scenario" trong appsettings.json.</summary>
public sealed class ScenarioOptions
{
    public const string SectionName = "Scenario";
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public int MaxContentSizeKb { get; set; } = 256;
    public int MaxVariantsPerBatch { get; set; } = 100;
}
