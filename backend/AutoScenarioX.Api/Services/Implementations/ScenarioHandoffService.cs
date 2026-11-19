using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Handoff;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Handoff;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioHandoffService(
    IUnitOfWork uow,
    IOptions<ScenarioOptions> options) : IScenarioHandoffService
{
    private readonly ScenarioOptions _options = options.Value;

    public async Task<PagedResult<ReadyScenarioVersionResponse>> SearchReadyAsync(ReadyVersionQueryRequest request, CancellationToken ct = default)
    {
        ValidateQuery(request);

        var page = Math.Clamp(request.Page, 1, 100_000);   // chặn trần để (page - 1) * pageSize không tràn int
        var pageSize = Math.Clamp(request.PageSize ?? _options.DefaultPageSize, 1, _options.MaxPageSize);

        var criteria = new ReadyVersionSearchCriteria(
            Keyword: request.Q,
            Category: request.Category,
            MapName: request.Map,
            ScenarioId: request.ScenarioId,
            LatestOnly: request.LatestOnly,
            Page: page,
            PageSize: pageSize);

        var data = await uow.ScenarioVersions.SearchReadyAsync(criteria, ct);
        var items = data.Items.Select(x => x.ToResponse()).ToList();
        return new PagedResult<ReadyScenarioVersionResponse>(items, page, pageSize, data.TotalCount);
    }

    public async Task<ExecutionPackageResponse> GetExecutionPackageAsync(Guid scenarioVersionId, CancellationToken ct = default)
    {
        var version = await uow.ScenarioVersions.GetReadOnlyAsync(scenarioVersionId, ct)
            ?? throw new NotFoundException(ErrorCodes.VersionNotFound, $"Scenario version {scenarioVersionId} was not found.");
        var scenario = await uow.Scenarios.GetReadOnlyAsync(version.ScenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {version.ScenarioId} was not found.");

        var map = version.MapName is { } mapName
            ? await uow.MapMetadata.GetActiveByNameAsync(mapName, ct)
            : null;

        var content = JsonNode.Parse(version.Content)!.AsObject();
        return version.ToExecutionPackage(scenario, map, ExecutionPackageBuilder.Build(content, map));
    }

    private static void ValidateQuery(ReadyVersionQueryRequest r)
    {
        var errors = new Dictionary<string, string[]>();

        if (!string.IsNullOrWhiteSpace(r.Category) && !ScenarioCatalog.Categories.Contains(r.Category))
            errors["category"] = [$"Invalid value. Allowed values: {string.Join(", ", ScenarioCatalog.Categories)}"];

        if (r.Q is { Length: > 100 })
            errors["q"] = ["Search text must be at most 100 characters"];

        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
