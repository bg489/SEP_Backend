using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;

namespace AutoScenarioX.Api.Common.Exceptions;

/// <summary>Batch không được ghi nếu có tổ hợp không hợp lệ.</summary>
public sealed class VariantValidationException(
    int variantIndex, JsonElement parameterValues, VariantValidationFailureResponse report)
    : AppException(ErrorCodes.VariantValidationFailed, $"Variant {variantIndex} is invalid. No batch or scenarios were created.")
{
    public int VariantIndex { get; } = variantIndex;
    public JsonElement ParameterValues { get; } = parameterValues;
    public VariantValidationFailureResponse Report { get; } = report;
}
