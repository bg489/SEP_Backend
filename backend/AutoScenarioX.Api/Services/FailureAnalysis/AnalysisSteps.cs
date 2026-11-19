using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;

namespace AutoScenarioX.Api.Services.FailureAnalysis;

public static class AnalysisSteps
{
    public const string Grouped = "grouped";
    public const string Verified = "verified";
    public const string SimilarFound = "similar_found";
    public const string Unexplained = "unexplained";
    public const string AwaitingFlow3 = "awaiting_flow3";
    public const string Explainable = "explainable";
    public const string ReportDraft = "report_draft";
    public const string NeedsMore = "needs_more";
    public const string Evaluated = "evaluated";

    public const string Returned = "returned_to_flow3";
    public const string InReport = "in_report";

    public static void Require(string actual, params string[] allowed)
    {
        if (allowed.Contains(actual))
            return;
        throw new BusinessRuleException(
            ErrorCodes.InvalidRequest,
            $"Analysis is at '{actual}'. This action needs: {string.Join(" or ", allowed)}.");
    }
}
