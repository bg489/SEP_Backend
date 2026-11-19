using System.Text.Json;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Mappings;

public static class VariantBatchMappings
{
    public static VariantBatchSummaryResponse ToSummaryResponse(this VariantBatch batch)
    {
        var grid = JsonHelper.ToElement(batch.ParameterGrid)!.Value;
        return new(batch.Id, batch.BaseVersionId, grid, batch.Strategy, CartesianCount(grid),
            batch.RequestedCount, batch.GeneratedCount, batch.Note, batch.CreatedBy, batch.CreatedAt);
    }

    /// <summary>Tích số giá trị các trục; grid pairwise lớn có thể vượt long nên chặn ở long.MaxValue.</summary>
    private static long CartesianCount(JsonElement grid)
    {
        if (grid.ValueKind != JsonValueKind.Object) return 0;
        long count = 1;
        foreach (var axis in grid.EnumerateObject())
        {
            var length = axis.Value.ValueKind == JsonValueKind.Array ? axis.Value.GetArrayLength() : 0;
            if (length == 0) return 0;
            count = count > long.MaxValue / length ? long.MaxValue : count * length;
        }
        return count;
    }

    public static VariantScenarioResponse ToVariantResponse(this Scenario scenario) => new(
        scenario.Id, scenario.Code, scenario.Name, scenario.Status,
        scenario.LatestVersionId, scenario.LatestVersionNumber, scenario.Draft is not null);
}
