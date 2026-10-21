using AutoScenarioX.AuthApi.Contracts;

namespace AutoScenarioX.AuthApi.Services;

public static class OracleRuleCatalog
{
    public static readonly List<OracleRuleResponse> Defaults =
    [
        new("collision", "Collision", "Detect vehicle or actor contact.", true, 0, "events"),
        new("lane-invasion", "Lane invasion", "Detect unintentional lane boundary crossings.", true, 0, "events"),
        new("unsafe-ttc", "Unsafe time to collision", "Minimum safe time-to-collision threshold.", true, 1.5, "seconds"),
        new("speeding", "Speed limit", "Maximum permitted speed for the scenario.", true, 50, "km/h"),
        new("traffic-light", "Traffic-light violation", "Detect red-light crossings.", true, 0, "events"),
        new("route-completion", "Route completion", "Minimum route progress required.", true, 95, "%"),
        new("stagnation", "Vehicle freezing", "Maximum stationary duration while route is incomplete.", true, 10, "seconds")
    ];

    // Actor scenes currently produce measured collision, speed and stationary-duration evidence.
    public static readonly HashSet<string> NativeRuleIds = ["collision", "speed", "speeding", "freezing", "stagnation"];
}
