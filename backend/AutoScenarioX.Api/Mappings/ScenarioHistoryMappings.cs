using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;

namespace AutoScenarioX.Api.Mappings;

public static class ScenarioHistoryMappings
{
    public static ScenarioHistoryEventResponse ToHistoryResponse(
        this AuditLog log, AppUser? actor, int? versionNumber, ScenarioRef? related) => new(
        log.Id, log.CreatedAt, log.Action, log.EntityType, log.EntityId,
        actor is null
            ? new HistoryActorResponse(log.ActorId, "Unknown user", null)
            : new HistoryActorResponse(actor.Id, actor.DisplayName, actor.Email),
        versionNumber,
        related is null ? null : new HistoryScenarioResponse(related.Id, related.Code, related.Name),
        JsonHelper.ToElement(log.Details)!.Value);
}
