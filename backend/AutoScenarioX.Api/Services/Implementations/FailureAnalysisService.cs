using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.FailureAnalysis;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.FailureAnalysis;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class FailureAnalysisService(IUnitOfWork uow, IOptions<TelemetryOptions> telemetry) : IFailureAnalysisService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> KnownChecks =
    [
        CheckNames.Collision, CheckNames.Speeding, CheckNames.Freezing, CheckNames.Ttc,
        CheckNames.LaneInvasion, CheckNames.RedLight, CheckNames.RouteIncomplete
    ];

    public async Task<FailureAnalysisResponse> GetAsync(Guid campaignId, CancellationToken ct = default)
    {
        var session = await uow.FailureAnalyses.GetByCampaignAsync(campaignId, tracking: false, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {campaignId} has no failure analysis yet.");
        return await ToResponse(session, ct);
    }

    public async Task<FailureAnalysisResponse> GroupAsync(Guid campaignId, CancellationToken ct = default)
    {
        _ = await RequireCampaign(campaignId, ct);
        var runs = await uow.Runs.ListForAnalysisAsync(campaignId, ct);
        if (runs.Count == 0)
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Campaign has no runs to analyze.");
        if (runs.Any(r => r.Status is SimulationRunStatuses.Queued or SimulationRunStatuses.Claimed or SimulationRunStatuses.Running))
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Flow 3 has not finished every run in this campaign.");

        var judged = await JudgeAsync(runs, ct);
        var failing = judged.Where(r => r.Verdict == Verdicts.Fail).ToList();
        var existing = await uow.FailureAnalyses.GetByCampaignAsync(campaignId, tracking: true, ct);
        if (existing is not null)
            uow.FailureAnalyses.Remove(existing);

        var now = DateTimeOffset.UtcNow;
        var session = new AnalysisSession
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            Status = AnalysisSteps.Grouped,
            CreatedAt = now,
            UpdatedAt = now,
            Summary = $"Đã gom {failing.Sum(r => r.Violations.Count(v => v.Severity == FailureAnalyzer.Failing))} lỗi từ {failing.Count} lượt FAIL. Bước tiếp theo: verify."
        };

        foreach (var run in failing)
        {
            foreach (var violation in run.Violations.Where(v => v.Severity == FailureAnalyzer.Failing)
                         .GroupBy(v => v.Check)
                         .Select(g => g.OrderBy(v => v.T).First()))
            {
                var fault = new AnalysisFault
                {
                    Id = Guid.NewGuid(),
                    RootScenarioId = run.RootScenarioId,
                    RootScenarioCode = run.RootScenarioCode,
                    RootScenarioName = run.RootScenarioName,
                    CheckName = violation.Check,
                    Status = AnalysisSteps.Grouped,
                    FirstRunId = run.RunId,
                    FirstT = (decimal)violation.T
                };
                fault.Runs.Add(new AnalysisFaultRun { FaultId = fault.Id, RunId = run.RunId, IsPrimary = true });
                session.Faults.Add(fault);
            }
        }

        uow.FailureAnalyses.Add(session);
        await uow.SaveChangesAsync(ct);
        return await ToResponse(session, ct);
    }

    public async Task<FailureAnalysisResponse> VerifyAsync(Guid campaignId, CancellationToken ct = default)
    {
        var session = await Tracked(campaignId, ct);
        AnalysisSteps.Require(session.Status, AnalysisSteps.Grouped);
        var unknown = session.Faults.Select(f => f.CheckName).Where(c => !KnownChecks.Contains(c)).Distinct().ToList();
        if (unknown.Count > 0)
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, $"Unknown check: {string.Join(", ", unknown)}.");

        foreach (var fault in session.Faults)
            fault.Status = AnalysisSteps.Verified;
        session.Status = AnalysisSteps.Verified;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.Summary = $"Đã verify {session.Faults.Count} lỗi. Bước tiếp theo: tìm lỗi giống nhau.";
        await uow.SaveChangesAsync(ct);
        return await ToResponse(session, ct);
    }

    public async Task<FailureAnalysisResponse> FindSimilarAsync(Guid campaignId, CancellationToken ct = default)
    {
        var session = await Tracked(campaignId, ct);
        AnalysisSteps.Require(session.Status, AnalysisSteps.Verified);

        foreach (var group in session.Faults.GroupBy(f => (f.RootScenarioId, f.CheckName)).ToList())
        {
            var ordered = group.OrderBy(f => f.FirstT).ThenBy(f => f.FirstRunId).ToList();
            var keeper = ordered[0];
            foreach (var other in ordered.Skip(1))
            {
                foreach (var link in other.Runs)
                {
                    if (keeper.Runs.Any(r => r.RunId == link.RunId))
                        continue;
                    keeper.Runs.Add(new AnalysisFaultRun { FaultId = keeper.Id, RunId = link.RunId, IsPrimary = false });
                }
                session.Faults.Remove(other);
            }
            keeper.Status = AnalysisSteps.SimilarFound;
        }

        session.Status = AnalysisSteps.SimilarFound;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.Summary = $"Đã gom thành {session.Faults.Count} fault. Bước tiếp theo: xét lỗi có giải thích được không.";
        await uow.SaveChangesAsync(ct);
        return await ToResponse(session, ct);
    }

    public async Task<FailureAnalysisResponse> CheckExplainabilityAsync(Guid campaignId, CancellationToken ct = default)
    {
        var session = await Tracked(campaignId, ct);
        AnalysisSteps.Require(session.Status, AnalysisSteps.SimilarFound);
        var judged = await JudgeAsync(await uow.Runs.ListForAnalysisAsync(campaignId, ct), ct);
        var byId = judged.ToDictionary(r => r.RunId);

        foreach (var fault in session.Faults)
        {
            var members = fault.Runs.Select(r => byId[r.RunId]).ToList();
            var violations = new List<JudgedViolation>();
            var explainable = members.Count > 0;
            foreach (var run in members)
            {
                var violation = run.Violations
                    .Where(v => v.Severity == FailureAnalyzer.Failing && v.Check == fault.CheckName)
                    .OrderBy(v => v.T)
                    .FirstOrDefault();
                if (violation is null || !FailureAnalyzer.CanExplain(run, violation))
                {
                    explainable = false;
                    break;
                }
                violations.Add(violation);
            }

            if (!explainable)
            {
                fault.Status = AnalysisSteps.Unexplained;
                fault.Explanation = null;
                continue;
            }

            var first = violations.OrderBy(v => v.T).First();
            fault.Status = AnalysisSteps.Explainable;
            fault.Explanation = FailureAnalyzer.Explain(first);
            fault.FirstT = (decimal)first.T;
        }

        var unexplained = session.Faults.Count(f => f.Status == AnalysisSteps.Unexplained);
        if (unexplained > 0)
        {
            session.Status = AnalysisSteps.Unexplained;
            session.Summary =
                $"Có {unexplained} fault chưa giải thích được. Chưa chạy lại. " +
                "Gọi return-to-flow3 khi muốn đưa các lượt đó về Flow 3.";
        }
        else
        {
            session.Status = AnalysisSteps.Explainable;
            session.Summary = session.Faults.Count == 0
                ? "Không có fault. Có thể lập report."
                : "Mọi fault đều giải thích được. Bước tiếp theo: lập report.";
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return await ToResponse(session, ct);
    }

    public async Task<FailureAnalysisResponse> ReturnToFlow3Async(Guid campaignId, CancellationToken ct = default)
    {
        var session = await Tracked(campaignId, ct);
        AnalysisSteps.Require(session.Status, AnalysisSteps.Unexplained);
        var runIds = session.Faults
            .Where(f => f.Status == AnalysisSteps.Unexplained)
            .SelectMany(f => f.Runs.Select(r => r.RunId))
            .ToHashSet();
        if (runIds.Count == 0)
            throw new BusinessRuleException(ErrorCodes.InvalidRequest, "No unexplained run to send back to Flow 3.");

        await uow.ExecuteInTransactionAsync(async token =>
        {
            await RequeueAsync(runIds, token);
            await uow.SaveChangesAsync(token);
            return true;
        }, ct);
        foreach (var fault in session.Faults.Where(f => f.Status == AnalysisSteps.Unexplained).ToList())
            fault.Status = AnalysisSteps.Returned;
        session.Status = AnalysisSteps.AwaitingFlow3;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.Summary =
            $"Đã đưa {runIds.Count} lượt về Flow 3. Gom lỗi lại sau khi Oracle chấm xong.";
        await uow.SaveChangesAsync(ct);
        return await ToResponse(session, ct);
    }

    public async Task<FailureAnalysisResponse> GenerateAsync(Guid campaignId, CancellationToken ct = default)
    {
        var session = await Tracked(campaignId, ct);
        AnalysisSteps.Require(session.Status, AnalysisSteps.Explainable);
        var codes = await RunCodesAsync(campaignId, ct);

        foreach (var fault in session.Faults)
        {
            var names = string.Join(", ", fault.Runs.Select(r => codes.GetValueOrDefault(r.RunId, r.RunId.ToString())).Distinct());
            fault.Status = AnalysisSteps.InReport;
            fault.ReportText =
                $"{fault.RootScenarioName} ({fault.RootScenarioCode}), rule {fault.CheckName}: {fault.Explanation} " +
                $"Thấy ở {fault.Runs.Count} lượt ({names}).";
        }

        session.Status = AnalysisSteps.ReportDraft;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.Summary = session.Faults.Count == 0
            ? "Report không có fault. Reviewer xem và chốt."
            : $"Đã lập report cho {session.Faults.Count} fault. Reviewer xem và chọn còn cần thêm hay hoàn thành.";
        await uow.SaveChangesAsync(ct);
        return await ToResponse(session, ct);
    }

    public async Task<FailureAnalysisResponse> ReviewAsync(
        Guid campaignId, ReviewFailureReportRequest request, CancellationToken ct = default)
    {
        var decision = request.Decision?.Trim().ToLowerInvariant() ?? "";
        if (decision is not ("needs_more" or "complete"))
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["decision"] = ["Decision must be needs_more or complete."]
            });

        var session = await Tracked(campaignId, ct);
        AnalysisSteps.Require(session.Status, AnalysisSteps.ReportDraft, AnalysisSteps.NeedsMore);
        session.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        session.UpdatedAt = DateTimeOffset.UtcNow;

        if (decision == "needs_more")
        {
            session.Status = AnalysisSteps.NeedsMore;
            session.Effectiveness = null;
            session.Summary = "Reviewer chưa chốt report. Chưa chạy đánh giá hiệu quả.";
            await uow.SaveChangesAsync(ct);
            return await ToResponse(session, ct);
        }

        var judged = await JudgeAsync(await uow.Runs.ListForAnalysisAsync(campaignId, ct), ct);
        var core = FailureAnalyzer.Analyze(judged);
        session.Status = AnalysisSteps.Evaluated;
        session.Effectiveness = JsonSerializer.Serialize(core.Effectiveness, Json);
        session.Summary = "Report đã chốt. " + core.Summary;
        await uow.SaveChangesAsync(ct);
        return await ToResponse(session, ct);
    }

    public async Task<EffectivenessResponse> GetEvaluationAsync(Guid campaignId, CancellationToken ct = default)
    {
        var session = await uow.FailureAnalyses.GetByCampaignAsync(campaignId, tracking: false, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {campaignId} has no failure analysis yet.");
        if (session.Status != AnalysisSteps.Evaluated || string.IsNullOrWhiteSpace(session.Effectiveness))
            throw new BusinessRuleException(
                ErrorCodes.InvalidRequest,
                "Evaluation is available only after the reviewer completes the report.");
        return JsonSerializer.Deserialize<EffectivenessResponse>(session.Effectiveness, Json)
            ?? throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Stored evaluation could not be read.");
    }

    private async Task<AnalysisSession> Tracked(Guid campaignId, CancellationToken ct)
    {
        _ = await RequireCampaign(campaignId, ct);
        return await uow.FailureAnalyses.GetByCampaignAsync(campaignId, tracking: true, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {campaignId} has no failure analysis yet.");
    }

    private async Task<Campaign> RequireCampaign(Guid campaignId, CancellationToken ct) =>
        await uow.Campaigns.GetReadOnlyAsync(campaignId, ct)
        ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {campaignId} was not found.");

    private async Task RequeueAsync(IReadOnlySet<Guid> runIds, CancellationToken ct)
    {
        Campaign? campaign = null;
        foreach (var runId in runIds)
        {
            var run = await uow.Runs.GetForUpdateAsync(runId, ct);
            if (run is null)
                continue;
            await uow.Runs.AddRetryAsync(run, ct);
            campaign = run.Campaign;
        }

        if (campaign is null)
            return;
        var currentRuns = campaign.Runs.Where(r => r.IsCurrent).ToList();
        var judged = currentRuns.Where(r => r.Verdict is not null).ToList();
        campaign.Progress = currentRuns.Count == 0 ? 0 : (int)Math.Round(100.0 * judged.Count / currentRuns.Count);
        campaign.FailedRuns = judged.Count(r => r.Verdict == Verdicts.Fail);
        campaign.PassRate = judged.Count == 0
            ? 0
            : Math.Round(100m * judged.Count(r => r.Verdict == Verdicts.Pass) / judged.Count, 2);
        campaign.Status = CampaignStatuses.Running;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> RunCodesAsync(Guid campaignId, CancellationToken ct)
    {
        var runs = await uow.Runs.ListForAnalysisAsync(campaignId, ct);
        return runs.ToDictionary(r => r.Id, r => r.ScenarioVersion.Scenario.Code);
    }

    private async Task<FailureAnalysisResponse> ToResponse(AnalysisSession session, CancellationToken ct)
    {
        var campaign = session.Campaign ?? await RequireCampaign(session.CampaignId, ct);
        var runs = await uow.Runs.ListForAnalysisAsync(session.CampaignId, ct);
        var judged = await JudgeAsync(runs, ct);
        var showExplanation = session.Status is AnalysisSteps.Explainable or AnalysisSteps.Unexplained
            or AnalysisSteps.AwaitingFlow3
            or AnalysisSteps.ReportDraft or AnalysisSteps.NeedsMore or AnalysisSteps.Evaluated;
        var showReport = session.Status is AnalysisSteps.ReportDraft or AnalysisSteps.NeedsMore or AnalysisSteps.Evaluated;
        var showSimilar = session.Status is not (AnalysisSteps.Grouped or AnalysisSteps.Verified);

        var faults = session.Faults
            .OrderBy(f => f.FirstT)
            .ThenBy(f => f.CheckName, StringComparer.Ordinal)
            .Select(f =>
            {
                var runIds = f.Runs.Select(r => r.RunId).Distinct().ToList();
                return new FaultGroupResponse(
                    f.RootScenarioId,
                    f.RootScenarioCode,
                    f.RootScenarioName,
                    f.CheckName,
                    runIds,
                    f.FirstRunId,
                    (double)f.FirstT,
                    showExplanation ? f.Explanation : null,
                    showReport ? f.ReportText : null,
                    f.Status is AnalysisSteps.Unexplained or AnalysisSteps.Returned,
                    f.Status,
                    showSimilar ? f.Runs.Where(r => !r.IsPrimary).Select(r => r.RunId).Distinct().ToList() : []);
            })
            .ToList();

        EffectivenessResponse? effectiveness = null;
        if (session.Status == AnalysisSteps.Evaluated && !string.IsNullOrWhiteSpace(session.Effectiveness))
            effectiveness = JsonSerializer.Deserialize<EffectivenessResponse>(session.Effectiveness, Json);

        return new FailureAnalysisResponse(
            session.Id,
            campaign.Id,
            campaign.Code,
            campaign.Name,
            session.Status,
            judged.Count,
            judged.Count(r => r.Verdict == Verdicts.Pass),
            judged.Count(r => r.Verdict == Verdicts.Fail),
            runs.Count(r => r.Evaluations.Any(e => e.Verdict == Verdicts.Error)),
            runs.Count(r => r.Evaluations.Count == 0),
            session.Summary,
            session.ReviewNote,
            judged.Select(r => new RunFeatureResponse(
                r.RunId, r.SortOrder, r.ScenarioCode, r.ScenarioName, r.Verdict, r.Outcome, r.DurationS, r.HasVideo,
                r.Violations.Where(v => v.Severity == FailureAnalyzer.Failing).Select(v => v.Check).Distinct().ToList(),
                r.Violations.Where(v => v.Severity != FailureAnalyzer.Failing).Select(v => v.Check).Distinct().ToList())).ToList(),
            faults,
            effectiveness);
    }

    private async Task<List<JudgedRun>> JudgeAsync(IReadOnlyList<SimulationRun> runs, CancellationToken ct)
    {
        var links = await LoadLinksAsync(runs, ct);
        var judged = new List<JudgedRun>();
        foreach (var run in runs.OrderBy(r => r.SortOrder))
        {
            var evaluation = run.Evaluations.OrderByDescending(e => e.EvaluatedAt).FirstOrDefault();
            if (evaluation is null || evaluation.Verdict == Verdicts.Error)
                continue;
            var version = run.ScenarioVersion;
            var rootId = FailureAnalyzer.ResolveRoot(version.Id, id => links.GetValueOrDefault(id));
            var root = links.Values.FirstOrDefault(l => l.ScenarioId == rootId)
                ?? new VersionLink(version.ScenarioId, version.SourceType, version.ParentVersionId, version.Scenario.Code, version.Scenario.Name);
            judged.Add(new JudgedRun(
                run.Id,
                run.SortOrder,
                version.ScenarioId,
                version.Scenario.Code,
                version.Scenario.Name,
                root.ScenarioId,
                root.ScenarioCode,
                root.ScenarioName,
                evaluation.Verdict,
                run.Telemetry?.Outcome,
                run.Telemetry?.DurationS is { } duration ? (double)duration : null,
                !string.IsNullOrWhiteSpace(run.Telemetry?.VideoPath),
                evaluation.Violations.Select(ToViolation).ToList()));
        }
        return judged;
    }

    private async Task<Dictionary<Guid, VersionLink>> LoadLinksAsync(IReadOnlyList<SimulationRun> runs, CancellationToken ct)
    {
        var links = new Dictionary<Guid, VersionLink>();
        var pending = new Queue<Guid>(runs.Select(r => r.ScenarioVersionId));
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (links.ContainsKey(id))
                continue;
            var version = await uow.ScenarioVersions.GetReadOnlyAsync(id, ct);
            if (version is null)
                continue;
            var scenario = version.Scenario ?? await uow.Scenarios.GetReadOnlyAsync(version.ScenarioId, ct);
            links[id] = new VersionLink(
                version.ScenarioId, version.SourceType, version.ParentVersionId, scenario?.Code ?? "", scenario?.Name ?? "");
            if (string.Equals(version.SourceType, "variant", StringComparison.OrdinalIgnoreCase)
                && version.ParentVersionId is Guid parent)
                pending.Enqueue(parent);
        }
        return links;
    }

    private static JudgedViolation ToViolation(ViolationEntity violation) => new(
        violation.CheckName,
        violation.Severity,
        (double)violation.T,
        violation.DurationS is { } duration ? (double)duration : null,
        Flatten(violation.Details));

    private static Dictionary<string, string> Flatten(string? json)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var child in property.Value.EnumerateObject())
                        result[$"{property.Name}.{child.Name}"] = child.Value.ToString();
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                    result[property.Name] = string.Join(",", property.Value.EnumerateArray().Select(item => item.ToString()));
                else
                    result[property.Name] = property.Value.ToString();
            }
        }
        catch (JsonException)
        {
            result["raw"] = json;
        }
        return result;
    }
}
