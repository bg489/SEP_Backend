using System.Text.Json;

namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

/// <summary>Body của PUT /api/v1/scenarios/{id}/draft.</summary>
public sealed class SaveDraftRequest
{
    /// <summary>Toàn bộ nội dung scenario theo schema v1.0 (một JSON object).</summary>
    public JsonElement Content { get; set; }
}
