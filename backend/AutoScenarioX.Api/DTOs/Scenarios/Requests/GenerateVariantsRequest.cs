using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

public sealed class GenerateVariantsRequest
{
    /// <summary>Danh sách giá trị tường minh cho từng tham số; tối đa 100 tổ hợp mặc định.</summary>
    public Dictionary<string, JsonElement>? ParameterGrid { get; set; }

    /// <summary>"cartesian" (mặc định, mọi tổ hợp) hoặc "pairwise" (phủ mọi cặp giá trị, ít tổ hợp hơn).</summary>
    public string? Strategy { get; set; }

    /// <summary>Tiền tố tên scenario, tối đa 100 ký tự. Bỏ trống = tên scenario gốc.</summary>
    public string? NamePrefix { get; set; }

    public string? Note { get; set; }
}
