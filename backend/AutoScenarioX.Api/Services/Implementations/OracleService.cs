using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Services.Oracle;
using Microsoft.Extensions.Options;
using OracleThresholds = AutoScenarioX.Api.Services.Oracle.OracleConfig;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class OracleService(IUnitOfWork uow, IOptions<TelemetryOptions> options, IRunArtifactStore? artifactStore = null) : IOracleService
{
    private readonly TelemetryOptions _options = options.Value;

    public async Task<IReadOnlyList<OracleConfigResponse>> ListConfigsAsync(CancellationToken ct = default)
        => (await uow.OracleConfigs.ListAsync(ct)).Select(c => c.ToResponse()).ToList();

    public async Task<RunResultResponse> GetRunResultAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await uow.Runs.GetResultAsync(runId, ct) ?? throw NotFound(runId);
        var version = (await uow.ScenarioVersions.GetReadOnlyAsync(run.ScenarioVersionId, ct))!;
        var scenario = (await uow.Scenarios.GetReadOnlyAsync(version.ScenarioId, ct))!;
        return run.ToResult(scenario, version);
    }

    public async Task<ClaimedRunResponse?> ClaimNextAsync(Guid? campaignId, CancellationToken ct = default, Guid? workerId = null)
    {
        var run = await uow.Runs.GetNextQueuedAsync(campaignId, ct, workerId);
        if (run is null) return null;

        run.Status = SimulationRunStatuses.Claimed;
        run.StartedAt ??= DateTimeOffset.UtcNow;
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);

        return ToClaimed(run);
    }

    public async Task<ClaimedRunResponse> MarkRunningAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await uow.Runs.GetForUpdateAsync(runId, ct) ?? throw NotFound(runId);
        if (run.Status is not (SimulationRunStatuses.Claimed or SimulationRunStatuses.Running))
            throw new ConflictException(
                ErrorCodes.RunNotClaimed,
                "Only a claimed run can be marked Running.");

        run.Status = SimulationRunStatuses.Running;
        run.StartedAt ??= DateTimeOffset.UtcNow;
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return ToClaimed(run);
    }

    public Task<RunResultResponse> SubmitTelemetryAsync(Guid runId, Stream body, CancellationToken ct = default)
        => uow.ExecuteInTransactionAsync(innerCt => SubmitTelemetryCoreAsync(runId, body, innerCt), ct);

    private async Task<RunResultResponse> SubmitTelemetryCoreAsync(Guid runId, Stream body, CancellationToken ct)
    {
        var run = await uow.Runs.GetForUpdateAsync(runId, ct) ?? throw NotFound(runId);
        if (run.Status == SimulationRunStatuses.Queued)
            throw new ConflictException(ErrorCodes.RunNotStarted, "This run has not been claimed by a runner yet.");
        var text = await ReadLimitedAsync(body, _options.MaxUploadMb, ct);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (run.Telemetry is not null)
        {
            if (run.TelemetrySha256 == hash) return await GetRunResultAsync(runId, ct);
            throw new ConflictException(ErrorCodes.TelemetryAlreadyReceived, "A different telemetry payload already exists for this run.");
        }
        if (!run.IsCurrent || run.Status is not (SimulationRunStatuses.Claimed or SimulationRunStatuses.Running))
            throw new ConflictException(ErrorCodes.RunNotStarted, "Only a current claimed/running attempt accepts its first telemetry.");

        string? outcome = null;
        decimal? duration = null;
        try
        {
            var read = TelemetryReader.Read(text);
            if ((string?)read.Header["run_id"] != runId.ToString())
                throw new BusinessRuleException(ErrorCodes.TelemetryRunMismatch, "Telemetry run_id does not match this run.");
            outcome = RunOutcomes.All.FirstOrDefault(o => o == (string?)read.End["outcome"]);
            duration = (decimal)(double)read.End["t"]!;
        }
        catch (TelemetryFormatException) { }

        var oracle = await uow.OracleConfigs.GetAsync(run.Campaign.OracleConfigId, ct)
            ?? throw new NotFoundException(ErrorCodes.OracleConfigNotFound, "Oracle config was not found.");
        var result = OracleEvaluator.Evaluate(text, new OracleThresholds(
            oracle.Version, (double)oracle.SpeedingToleranceKmh, (double)oracle.SpeedingMinDurationS,
            (double)oracle.FreezingMaxStillS, (double)oracle.TtcThresholdS, (double)oracle.RouteGoalToleranceM));

        var evaluation = new Evaluation
        {
            Id = Guid.NewGuid(), RunId = run.Id, OracleConfigId = oracle.Id,
            Verdict = result.Verdict switch { Verdict.Pass => Verdicts.Pass, Verdict.Fail => Verdicts.Fail, _ => Verdicts.Error },
            ErrorReason = result.ErrorReason,
        };
        foreach (var v in result.Violations)
            evaluation.Violations.Add(new ViolationEntity
            {
                Id = Guid.NewGuid(), CheckName = v.Check,
                Severity = v.Severity == ViolationSeverity.Failing ? ViolationSeverities.Failing : ViolationSeverities.Warning,
                T = (decimal)v.T, DurationS = v.DurationS is { } d ? (decimal)d : null, Details = v.Details.ToJsonString(),
            });

        var path = await StoreTelemetryAsync(run, text, ct);
        run.TelemetrySha256 = hash;
        uow.Runs.AddTelemetry(new RunTelemetry { RunId = run.Id, TelemetryPath = path, Outcome = outcome, DurationS = duration });
        uow.Runs.AddEvaluation(evaluation);
        run.Verdict = evaluation.Verdict;
        run.FinishedAt ??= DateTimeOffset.UtcNow;
        run.Status = evaluation.Verdict switch
        {
            Verdicts.Error => SimulationRunStatuses.Error,
            _ when outcome == RunOutcomes.TimedOut => SimulationRunStatuses.TimedOut,
            _ when outcome == RunOutcomes.InfraFailed => SimulationRunStatuses.Failed,
            _ => SimulationRunStatuses.Completed,
        };
        run.UpdatedAt = DateTimeOffset.UtcNow;
        SyncCampaignProgress(run.Campaign);
        await uow.SaveChangesAsync(ct);

        return await GetRunResultAsync(runId, ct);
    }

    public Task<RequeueRunResponse> RequeueInfraAsync(Guid runId, CancellationToken ct = default)
        => uow.ExecuteInTransactionAsync(innerCt => RequeueInfraCoreAsync(runId, innerCt), ct);

    private async Task<RequeueRunResponse> RequeueInfraCoreAsync(Guid runId, CancellationToken ct)
    {
        var run = await uow.Runs.GetForUpdateAsync(runId, ct) ?? throw NotFound(runId);
        if (run.Status != SimulationRunStatuses.Error || run.Verdict != Verdicts.Error)
            throw new ConflictException(
                ErrorCodes.RunNotRequeueable,
                "Only runs with infrastructure ERROR can be re-queued.");

        var outcome = run.Telemetry?.Outcome;
        var reason = run.Evaluations.OrderByDescending(e => e.EvaluatedAt).FirstOrDefault()?.ErrorReason ?? "";
        var isInfra = outcome == RunOutcomes.InfraFailed
                      || reason.Contains("simulator", StringComparison.OrdinalIgnoreCase)
                      || reason.Contains("connection", StringComparison.OrdinalIgnoreCase)
                      || reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                      || reason.Contains("CARLA", StringComparison.OrdinalIgnoreCase);
        if (!isInfra)
            throw new ConflictException(
                ErrorCodes.RunNotRequeueable,
                "This ERROR is not an infrastructure failure and cannot be re-queued.");

        if (run.InfraRetryCount >= _options.MaxInfraRetries)
            throw new ConflictException(
                ErrorCodes.InfraRetryExhausted,
                $"Infrastructure retry limit ({_options.MaxInfraRetries}) reached for this run.");

        var retry = await uow.Runs.AddRetryAsync(run, ct);
        retry.InfraRetryCount = run.InfraRetryCount + 1;

        if (run.Campaign.Status == CampaignStatuses.Completed)
            run.Campaign.Status = CampaignStatuses.Running;
        SyncCampaignProgress(run.Campaign);
        await uow.SaveChangesAsync(ct);

        return new RequeueRunResponse(retry.Id, retry.InfraRetryCount, _options.MaxInfraRetries, retry.Status, run.Id);
    }

    public Task SaveVideoAsync(Guid runId, Stream body, CancellationToken ct = default)
        => uow.ExecuteInTransactionAsync(async innerCt => { await SaveVideoCoreAsync(runId, body, innerCt); return 0; }, ct);

    private async Task SaveVideoCoreAsync(Guid runId, Stream body, CancellationToken ct)
    {
        var run = await uow.Runs.GetForUpdateAsync(runId, ct) ?? throw NotFound(runId);
        if (run.Telemetry is null)
            throw new ConflictException(ErrorCodes.RunNotStarted, "Submit telemetry before uploading video.");

        var relative = Path.Combine(run.CampaignId.ToString(), $"{run.Id}.mp4");
        var full = Path.Combine(Path.GetFullPath(_options.Directory), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var max = _options.MaxVideoUploadMb * 1024L * 1024L;
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".upload";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using var file = File.Create(temporary);
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = await body.ReadAsync(buffer, ct)) > 0)
            {
                total += n;
                if (total > max)
                {
                    file.Close();
                    throw new BusinessRuleException(
                        ErrorCodes.TelemetryTooLarge,
                        $"Video must be at most {_options.MaxVideoUploadMb} MB.");
                }
                await file.WriteAsync(buffer.AsMemory(0, n), ct);
                hash.AppendData(buffer, 0, n);
            }
            if (total == 0) throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Video cannot be empty.");
        }
        catch { File.Delete(temporary); throw; }
        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (run.Telemetry.VideoPath is not null)
        {
            File.Delete(temporary);
            if (run.Telemetry.VideoSha256 == digest) return;
            throw new ConflictException(ErrorCodes.TelemetryAlreadyReceived, "A different video already exists for this attempt.");
        }
        File.Move(temporary, full, overwrite: false);
        if (artifactStore is not null) await artifactStore.UploadAsync(relative.Replace('\\', '/'), full, "video/mp4", ct);
        run.Telemetry.VideoPath = relative.Replace('\\', '/');
        run.Telemetry.VideoSha256 = digest;
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    public async Task<(Stream Stream, string ContentType)?> OpenVideoAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await uow.Runs.GetResultAsync(runId, ct) ?? throw NotFound(runId);
        var relative = run.Telemetry?.VideoPath;
        if (string.IsNullOrEmpty(relative))
            return null;
        if (artifactStore?.IsCloud == true)
        {
            var remote = await artifactStore.OpenAsync(relative, ct);
            return remote is null ? null : (remote, "video/mp4");
        }

        var full = Path.Combine(Path.GetFullPath(_options.Directory), relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
            return null;

        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, "video/mp4");
    }

    private static void SyncCampaignProgress(Campaign campaign)
    {
        var runs = campaign.Runs.Where(r => r.IsCurrent).ToList();
        if (runs.Count == 0) return;

        var judged = runs.Where(r => r.Verdict is not null).ToList();
        campaign.Progress = (int)Math.Round(100.0 * judged.Count / runs.Count);
        campaign.FailedRuns = judged.Count(r => r.Verdict == Verdicts.Fail);
        campaign.PassRate = judged.Count == 0
            ? 0
            : Math.Round(100m * judged.Count(r => r.Verdict == Verdicts.Pass) / judged.Count, 2);

        if (judged.Count == runs.Count)
            campaign.Status = CampaignStatuses.Completed;
        else if (campaign.Status == CampaignStatuses.Completed)
            campaign.Status = CampaignStatuses.Running;
    }

    private ClaimedRunResponse ToClaimed(SimulationRun run)
    {
        var c = run.Campaign;
        return new ClaimedRunResponse(
            run.Id, run.CampaignId, c.Code, c.Name,
            run.ScenarioVersionId, run.ContentHash, run.Seed, run.SortOrder,
            c.SutType, c.SutBehavior, c.SutCarlaVersion,
            run.InfraRetryCount, _options.MaxInfraRetries);
    }

    private static async Task<string> ReadLimitedAsync(Stream body, int maxMb, CancellationToken ct)
    {
        var max = maxMb * 1024L * 1024L;
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await body.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > max)
                throw new BusinessRuleException(ErrorCodes.TelemetryTooLarge, $"Telemetry must be at most {maxMb} MB.");
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private async Task<string> StoreTelemetryAsync(SimulationRun run, string text, CancellationToken ct)
    {
        var relative = Path.Combine(run.CampaignId.ToString(), $"{run.Id}.jsonl.gz");
        var full = Path.Combine(Path.GetFullPath(_options.Directory), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var file = File.Create(full))
        await using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            await gz.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        if (artifactStore is not null) await artifactStore.UploadAsync(relative.Replace('\\', '/'), full, "application/gzip", ct);
        return relative.Replace('\\', '/');
    }

    public async Task<RunProgressResponse> GetProgressAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await uow.Runs.GetResultAsync(runId, ct) ?? throw NotFound(runId);
        return new(run.Id, run.Status, run.ProgressAt, string.IsNullOrEmpty(run.ProgressJson)
            ? null : JsonSerializer.Deserialize<RunProgressRequest>(run.ProgressJson));
    }

    public Task<RunProgressResponse> SaveProgressAsync(Guid runId, RunProgressRequest request, CancellationToken ct = default)
        => uow.ExecuteInTransactionAsync(async innerCt =>
        {
            var run = await uow.Runs.GetForUpdateAsync(runId, innerCt) ?? throw NotFound(runId);
            if (!run.IsCurrent || run.Status is not (SimulationRunStatuses.Claimed or SimulationRunStatuses.Running))
                throw new ConflictException(ErrorCodes.RunNotStarted, "Progress is accepted only for a current active attempt.");
            if (request.Frame < 0 || request.SimulationTimeS < 0 || request.SpeedKmh < 0 ||
                new[] { request.SimulationTimeS, request.EgoX, request.EgoY, request.EgoZ, request.SpeedKmh }.Any(v => !double.IsFinite(v)) || request.Message?.Length > 500)
                throw new BusinessRuleException(ErrorCodes.InvalidRequest, "Progress must contain finite coordinates, nonnegative time/frame/speed and at most 500 message characters.");
            var previous = string.IsNullOrEmpty(run.ProgressJson) ? null : JsonSerializer.Deserialize<RunProgressRequest>(run.ProgressJson);
            if (previous is not null && request.Frame < previous.Frame)
                throw new ConflictException(ErrorCodes.InvalidRequest, "Stale progress frame was rejected.");
            run.ProgressJson = JsonSerializer.Serialize(request);
            run.ProgressAt = DateTimeOffset.UtcNow;
            await uow.SaveChangesAsync(innerCt);
            return new RunProgressResponse(run.Id, run.Status, run.ProgressAt, request);
        }, ct);

    private static NotFoundException NotFound(Guid id) => new(ErrorCodes.RunNotFound, $"Simulation run {id} was not found.");
}
