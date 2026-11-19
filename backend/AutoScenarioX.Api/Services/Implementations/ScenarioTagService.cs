using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.DTOs.Tags.Requests;
using AutoScenarioX.Api.DTOs.Tags.Responses;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Services.Tags;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioTagService(IUnitOfWork uow, ICurrentUser currentUser) : IScenarioTagService
{
    public async Task<IReadOnlyList<TagResponse>> ListAsync(CancellationToken ct = default)
        => (await uow.Tags.ListUsageAsync(ct))
            .Select(t => new TagResponse(t.Name, t.ScenarioCount, t.ActiveScenarioCount))
            .ToList();

    public async Task<ScenarioTagsResponse> ReplaceAsync(
        Guid scenarioId, ReplaceScenarioTagsRequest request, CancellationToken ct = default)
    {
        var desired = TagNames.FromRequest(request.Tags);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            await uow.Scenarios.LockForUpdateAsync(scenarioId, token);
            var scenario = await uow.Scenarios.GetReadOnlyAsync(scenarioId, token)
                ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");
            if (scenario.Status == ScenarioStatuses.Archived)
                throw new BusinessRuleException(ErrorCodes.ScenarioArchived,
                    "This scenario is archived. Restore it before changing tags.");

            var current = await uow.Tags.ListNamesAsync(scenarioId, token);
            var added = desired.Where(t => !current.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
            var removed = current.Where(t => !desired.Contains(t)).ToList();
            if (added.Count == 0 && removed.Count == 0)
                return new ScenarioTagsResponse(scenarioId, current);

            await uow.Tags.AttachAsync(scenarioId, added, token);
            await uow.Tags.DetachAsync(scenarioId, removed, token);
            if (added.Count > 0)
                uow.AuditLogs.Write(AuditEntityTypes.Scenario, scenarioId, AuditActions.Tag, currentUser.UserId, new { tags = added });
            if (removed.Count > 0)
                uow.AuditLogs.Write(AuditEntityTypes.Scenario, scenarioId, AuditActions.Untag, currentUser.UserId, new { tags = removed });
            await uow.SaveChangesAsync(token);

            return new ScenarioTagsResponse(scenarioId, desired.Order(StringComparer.Ordinal).ToList());
        }, ct);
    }
}
