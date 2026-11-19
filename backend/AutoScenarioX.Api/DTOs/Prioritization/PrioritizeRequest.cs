namespace AutoScenarioX.Api.DTOs.Prioritization;

/// <summary>
/// Yêu cầu xếp hạng scenario version READY cho campaign / suite test.
/// Nếu ScenarioVersionIds trống: lấy các bản READY mới nhất (latestOnly) trong kho.
/// </summary>
public sealed class PrioritizeRequest
{
    /// <summary>Các scenario_version_id cần xếp hạng. Tối đa 100.</summary>
    public IReadOnlyList<Guid>? ScenarioVersionIds { get; init; }

    /// <summary>
    /// proposed_failure_history | severity_first | risk_first | diversity | random_baseline
    /// </summary>
    public string Strategy { get; init; } = PrioritizationStrategies.ProposedFailureHistory;

    /// <summary>Tham số strategy (tuỳ chọn).</summary>
    public PrioritizationParams? Params { get; init; }
}

public sealed class PrioritizationParams
{
    /// <summary>Trọng số đa dạng (0–1). Mặc định 0.45.</summary>
    public double? DiversityWeight { get; init; }

    /// <summary>Trọng số rủi ro heuristic (0–1). Mặc định 0.55.</summary>
    public double? RiskWeight { get; init; }

    /// <summary>Seed cho random_baseline. Mặc định 42.</summary>
    public int? Seed { get; init; }

    /// <summary>Giới hạn số version khi tự lấy READY. Mặc định 40, tối đa 100.</summary>
    public int? Limit { get; init; }
}

public static class PrioritizationStrategies
{
    public const string ProposedFailureHistory = "proposed_failure_history";
    public const string SeverityFirst = "severity_first";
    public const string RiskFirst = "risk_first";
    public const string Diversity = "diversity";
    public const string RandomBaseline = "random_baseline";

    public static readonly IReadOnlyList<string> All =
    [
        ProposedFailureHistory,
        SeverityFirst,
        RiskFirst,
        Diversity,
        RandomBaseline
    ];
}
