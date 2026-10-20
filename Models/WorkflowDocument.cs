namespace AutoScenarioX.AuthApi.Models;

public sealed class WorkflowDocument
{
    public string Area { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}
