namespace AutoScenarioX.Api.Tests.Support;

/// <summary>Missing live evidence must appear as SKIPPED, never an exercised PASS.</summary>
public sealed class CarlaTelemetryFactAttribute : FactAttribute
{
    public CarlaTelemetryFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASX_TELEMETRY_DIR")))
            Skip = "Live CARLA evidence was not supplied: set ASX_TELEMETRY_DIR to run this acceptance check.";
    }
}
