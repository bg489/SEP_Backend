namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.variant_batches - một lần sinh biến thể (parameter sweep).</summary>
public class VariantBatch
{
    public Guid Id { get; set; }
    public Guid BaseVersionId { get; set; }
    public string ParameterGrid { get; set; } = "{}";       // jsonb
    public string Strategy { get; set; } = VariantStrategies.Cartesian;
    public int RequestedCount { get; set; }
    public int GeneratedCount { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>asx.variant_batches.strategy - PHẢI khớp CHECK trong migration 105.</summary>
public static class VariantStrategies
{
    /// <summary>Tích Descartes: mọi tổ hợp giá trị.</summary>
    public const string Cartesian = "cartesian";
    /// <summary>Phủ mọi cặp giá trị của hai tham số bất kỳ (IPOG, tất định).</summary>
    public const string Pairwise = "pairwise";

    public static readonly IReadOnlyList<string> All = [Cartesian, Pairwise];
}
