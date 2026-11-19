using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Campaigns;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class CampaignService(IUnitOfWork uow, ICurrentUser currentUser) : ICampaignService
{
    private static readonly HashSet<string> AllowedSutTypes = [SutTypes.BehaviorAgent, SutTypes.Autopilot];
    private static readonly HashSet<string> AllowedBehaviors =
        [SutBehaviors.Cautious, SutBehaviors.Normal, SutBehaviors.Aggressive];

    public async Task<IReadOnlyList<CampaignResponse>> ListAsync(CancellationToken ct = default)
    {
        var items = await uow.Campaigns.ListAsync(ct);
        return items.Select(ToResponse).ToList();
    }

    public async Task<CampaignResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await uow.Campaigns.GetReadOnlyAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {id} was not found.");
        return ToResponse(campaign);
    }

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default)
    {
        ValidateCreate(request);
        var (sutType, sutBehavior, sutCarla) = NormalizeSut(
            request.SutType, request.SutBehavior, request.SutCarlaVersion);

        var ids = request.ScenarioVersionIds.Distinct().ToArray();
        var versions = await LoadReadyVersionsAsync(ids, ct);
        var orderMap = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var estimatedMinutes = versions.Sum(v => 4.0 + v.ActorCount * 0.48 + v.EventCount * 0.35);

        var oracle = (request.OracleConfigId is { } selectedOracle
            ? await uow.OracleConfigs.GetAsync(selectedOracle, ct)
            : await uow.OracleConfigs.GetLatestAsync(ct))
            ?? throw new BusinessRuleException(
                ErrorCodes.OracleConfigNotFound,
                "No Oracle Config is available. Apply migration 108_flow3_oracle.sql first.");

        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Strategy = string.IsNullOrWhiteSpace(request.Strategy)
                ? "proposed_failure_history"
                : request.Strategy.Trim(),
            Status = CampaignStatuses.Queued,
            Progress = 0,
            PassRate = 0,
            FailedRuns = 0,
            EstimatedDuration = FormatDuration(estimatedMinutes),
            SutType = sutType,
            SutBehavior = sutBehavior,
            SutCarlaVersion = sutCarla,
            OracleConfigId = oracle.Id,
            CreatedBy = currentUser.UserId,
            Versions = ids.Select(id => new CampaignVersion
            {
                ScenarioVersionId = id,
                SortOrder = orderMap[id]
            }).ToList()
        };

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            await uow.Campaigns.AddAsync(campaign, innerCt);
            await uow.SaveChangesAsync(innerCt);
            return 0;
        }, ct);

        return await GetAsync(campaign.Id, ct);
    }

    public async Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken ct = default)
    {
        var campaign = await uow.Campaigns.GetTrackedAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {id} was not found.");

        if (campaign.Status is CampaignStatuses.Running or CampaignStatuses.Completed)
            throw new BusinessRuleException(
                ErrorCodes.InvalidRequest,
                "Running or completed campaigns cannot be edited. Create a new campaign instead.");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            if (request.Name.Trim().Length is < 3 or > 120)
                throw new RequestValidationException(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name must be between 3 and 120 characters."]
                });
            campaign.Name = request.Name.Trim();
        }

        if (request.Description is not null)
            campaign.Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();

        if (!string.IsNullOrWhiteSpace(request.Strategy))
            campaign.Strategy = request.Strategy.Trim();

        if (request.OracleConfigId is { } oracleId && oracleId != campaign.OracleConfigId)
        {
            if (await uow.Campaigns.HasRunsAsync(id, ct))
                throw new ConflictException(ErrorCodes.InvalidRequest, "Oracle configuration is pinned after the first run; create a new campaign to change it.");
            _ = await uow.OracleConfigs.GetAsync(oracleId, ct)
                ?? throw new NotFoundException(ErrorCodes.OracleConfigNotFound, "Selected Oracle configuration was not found.");
            campaign.OracleConfigId = oracleId;
        }

        if (request.SutType is not null || request.SutBehavior is not null || request.SutCarlaVersion is not null)
        {
            var (sutType, sutBehavior, sutCarla) = NormalizeSut(
                request.SutType ?? campaign.SutType,
                request.SutBehavior ?? campaign.SutBehavior,
                request.SutCarlaVersion ?? campaign.SutCarlaVersion);
            campaign.SutType = sutType;
            campaign.SutBehavior = sutBehavior;
            campaign.SutCarlaVersion = sutCarla;
        }

        if (request.ScenarioVersionIds is { Count: > 0 })
        {
            if (await uow.Campaigns.HasRunsAsync(id, ct))
                throw new BusinessRuleException(
                    ErrorCodes.InvalidRequest,
                    "Cannot change scenario list after the campaign has been started.");

            var ids = request.ScenarioVersionIds.Distinct().ToArray();
            var versions = await LoadReadyVersionsAsync(ids, ct);
            var orderMap = ids.Select((vid, i) => (vid, i)).ToDictionary(x => x.vid, x => x.i);

            uow.Campaigns.RemoveVersions(campaign.Versions.ToList());
            campaign.Versions.Clear();
            foreach (var vid in ids)
            {
                campaign.Versions.Add(new CampaignVersion
                {
                    CampaignId = campaign.Id,
                    ScenarioVersionId = vid,
                    SortOrder = orderMap[vid]
                });
            }

            var estimatedMinutes = versions.Sum(v => 4.0 + v.ActorCount * 0.48 + v.EventCount * 0.35);
            campaign.EstimatedDuration = FormatDuration(estimatedMinutes);
        }

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            await uow.SaveChangesAsync(innerCt);
            return 0;
        }, ct);

        return await GetAsync(id, ct);
    }

    public async Task<CampaignResponse> StartAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await uow.Campaigns.GetTrackedAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {id} was not found.");

        if (campaign.Status == CampaignStatuses.Running)
            return await GetAsync(id, ct);

        if (campaign.Status is CampaignStatuses.Completed or CampaignStatuses.NeedsReview)
            throw new BusinessRuleException(
                ErrorCodes.InvalidRequest,
                "This campaign already finished. Create a new campaign to run again.");

        if (campaign.Versions.Count == 0)
            throw new BusinessRuleException(
                ErrorCodes.InvalidRequest,
                "Campaign has no scenario versions to run.");

        if (await uow.Campaigns.HasRunsAsync(id, ct))
            throw new BusinessRuleException(
                ErrorCodes.InvalidRequest,
                "Simulation runs already exist for this campaign.");

        var versionIds = campaign.Versions.Select(v => v.ScenarioVersionId).ToArray();
        var versions = await uow.ScenarioVersions.ListByIdsAsync(versionIds, ct);
        var hashById = versions.ToDictionary(v => v.Id, v => v.ContentHash);

        var runs = campaign.Versions
            .OrderBy(v => v.SortOrder)
            .Select(v => new SimulationRun
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                ScenarioVersionId = v.ScenarioVersionId,
                SortOrder = v.SortOrder,
                Status = SimulationRunStatuses.Queued,
                ContentHash = hashById.GetValueOrDefault(v.ScenarioVersionId),
                Seed = null
            })
            .ToList();

        campaign.Status = CampaignStatuses.Running;
        campaign.StartedAt = DateTimeOffset.UtcNow;
        campaign.Progress = 0;

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            await uow.Campaigns.AddRunsAsync(runs, innerCt);
            await uow.SaveChangesAsync(innerCt);
            return 0;
        }, ct);

        return await GetAsync(id, ct);
    }

    public async Task<CampaignResponse> RerunAsync(Guid id, CancellationToken ct = default)
    {
        var campaign = await uow.Campaigns.GetTrackedAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {id} was not found.");

        if (campaign.Versions.Count == 0)
            throw new BusinessRuleException(
                ErrorCodes.InvalidRequest,
                "Campaign has no scenario versions to run.");

        var versionIds = campaign.Versions.Select(v => v.ScenarioVersionId).ToArray();
        var versions = await uow.ScenarioVersions.ListByIdsAsync(versionIds, ct);
        var hashById = versions.ToDictionary(v => v.Id, v => v.ContentHash);

        if (campaign.Runs.Any(r => r.IsCurrent && r.Status is SimulationRunStatuses.Claimed or SimulationRunStatuses.Running or SimulationRunStatuses.Queued))
            throw new ConflictException(ErrorCodes.InvalidRequest, "Wait for current attempts to finish before rerunning the campaign.");

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var version in campaign.Versions.OrderBy(v => v.SortOrder))
            {
                var previous = campaign.Runs.Where(r => r.ScenarioVersionId == version.ScenarioVersionId && r.IsCurrent)
                    .OrderByDescending(r => r.Attempt).FirstOrDefault();
                if (previous is not null)
                    await uow.Runs.AddRetryAsync(previous, innerCt);
                else
                    await uow.Campaigns.AddRunsAsync([new SimulationRun
                    {
                        Id = Guid.NewGuid(), CampaignId = campaign.Id, ScenarioVersionId = version.ScenarioVersionId,
                        SortOrder = version.SortOrder, ContentHash = hashById.GetValueOrDefault(version.ScenarioVersionId),
                    }], innerCt);
            }

        campaign.Status = CampaignStatuses.Running;
        campaign.StartedAt = DateTimeOffset.UtcNow;
        campaign.Progress = 0;
        campaign.PassRate = 0;
        campaign.FailedRuns = 0;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;

            await uow.SaveChangesAsync(innerCt);
            return 0;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<SimulationRunResponse>> ListRunsAsync(Guid campaignId, CancellationToken ct = default)
    {
        _ = await uow.Campaigns.GetReadOnlyAsync(campaignId, ct)
            ?? throw new NotFoundException(ErrorCodes.InvalidRequest, $"Campaign {campaignId} was not found.");

        var runs = await uow.Campaigns.ListRunsAsync(campaignId, ct);
        return runs.Select(ToRunResponse).ToList();
    }

    private async Task<IReadOnlyList<ScenarioVersion>> LoadReadyVersionsAsync(Guid[] ids, CancellationToken ct)
    {
        var versions = await uow.ScenarioVersions.ListByIdsAsync(ids, ct);
        if (versions.Count != ids.Length)
        {
            var missing = ids.Except(versions.Select(v => v.Id)).Take(5);
            throw new NotFoundException(
                ErrorCodes.VersionNotFound,
                $"Scenario version(s) not found: {string.Join(", ", missing)}");
        }

        foreach (var version in versions)
        {
            if (version.Status != VersionStatuses.Ready)
                throw new BusinessRuleException(
                    ErrorCodes.InvalidRequest,
                    $"Version {version.Id} is {version.Status}; only READY versions can join a new campaign.");

            var scenario = await uow.Scenarios.GetReadOnlyAsync(version.ScenarioId, ct)
                ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {version.ScenarioId} was not found.");
            if (scenario.Status != ScenarioStatuses.Active)
                throw new BusinessRuleException(
                    ErrorCodes.ScenarioArchived,
                    $"Scenario {scenario.Code} is archived and cannot join a new campaign.");
        }

        return versions;
    }

    private static void ValidateCreate(CreateCampaignRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 3)
            errors["name"] = ["Name must be at least 3 characters."];
        if (request.Name?.Length > 120)
            errors["name"] = ["Name must be at most 120 characters."];
        if (request.ScenarioVersionIds is not { Count: > 0 })
            errors["scenarioVersionIds"] = ["Select at least one READY scenario version."];
        if (request.ScenarioVersionIds is { Count: > 100 })
            errors["scenarioVersionIds"] = ["At most 100 versions per campaign."];
        ValidateSutFields(request.SutType, request.SutBehavior, request.SutCarlaVersion, errors);
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private static void ValidateSutFields(
        string? sutType, string? sutBehavior, string? carlaVersion, IDictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(sutType) && !AllowedSutTypes.Contains(sutType.Trim()))
            errors["sutType"] = ["SUT type must be behavior_agent or autopilot."];
        if (!string.IsNullOrWhiteSpace(sutBehavior) && !AllowedBehaviors.Contains(sutBehavior.Trim()))
            errors["sutBehavior"] = ["SUT behavior must be cautious, normal, or aggressive."];
        if (!string.IsNullOrWhiteSpace(carlaVersion) && carlaVersion.Trim().Length > 32)
            errors["sutCarlaVersion"] = ["CARLA version must be at most 32 characters."];
    }

    private static (string Type, string Behavior, string Carla) NormalizeSut(
        string? sutType, string? sutBehavior, string? carlaVersion)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateSutFields(sutType, sutBehavior, carlaVersion, errors);
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var type = string.IsNullOrWhiteSpace(sutType) ? SutTypes.BehaviorAgent : sutType.Trim();
        var behavior = string.IsNullOrWhiteSpace(sutBehavior) ? SutBehaviors.Normal : sutBehavior.Trim();
        var carla = string.IsNullOrWhiteSpace(carlaVersion) ? "0.9.16" : carlaVersion.Trim();
        return (type, behavior, carla);
    }

    private static CampaignResponse ToResponse(Campaign c)
    {
        var ordered = c.Versions.OrderBy(v => v.SortOrder).ToList();
        var scenarios = ordered.Select(v =>
        {
            var ver = v.ScenarioVersion;
            var scn = ver.Scenario;
            return new CampaignScenarioItem(
                ver.Id,
                ver.ScenarioId,
                scn?.Code ?? "",
                scn?.Name ?? "",
                scn?.Category ?? "",
                ver.VersionNumber,
                ver.MapName,
                ver.WeatherPreset,
                v.SortOrder);
        }).ToList();

        return new CampaignResponse(
            c.Id,
            c.Code,
            c.Name,
            c.Description,
            c.Strategy,
            c.Status,
            c.Progress,
            c.PassRate,
            c.FailedRuns,
            c.EstimatedDuration,
            c.Creator?.DisplayName ?? c.Creator?.Email ?? "Unknown",
            c.CreatedAt,
            c.StartedAt,
            new CampaignSutConfig(c.SutType, c.SutBehavior, c.SutCarlaVersion),
            ordered.Select(v => v.ScenarioVersionId).ToList(),
            scenarios,
            c.Runs?.Count(r => r.IsCurrent) ?? 0,
            c.OracleConfigId);
    }

    private static SimulationRunResponse ToRunResponse(SimulationRun r)
    {
        var ver = r.ScenarioVersion;
        var scn = ver?.Scenario;
        return new SimulationRunResponse(
            r.Id,
            r.CampaignId,
            r.ScenarioVersionId,
            ver?.ScenarioId ?? Guid.Empty,
            scn?.Code ?? "",
            scn?.Name ?? "",
            scn?.Category ?? "",
            ver?.VersionNumber ?? 0,
            ver?.MapName,
            ver?.WeatherPreset,
            r.SortOrder,
            r.Status,
            r.Verdict,
            r.ContentHash,
            r.Seed,
            r.StartedAt,
            r.FinishedAt,
            r.ErrorMessage,
            r.Attempt,
            r.PreviousRunId,
            r.IsCurrent,
            r.WorkerId);
    }

    private static string FormatDuration(double minutes)
    {
        if (minutes < 60) return $"{Math.Max(1, Math.Round(minutes))}m";
        var h = Math.Floor(minutes / 60);
        var m = Math.Round(minutes % 60);
        return $"{h}h {m}m";
    }
}
