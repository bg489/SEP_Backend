using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Services.Tags;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IOptions<ScenarioOptions> options) : IScenarioService
{
    private readonly ScenarioOptions _options = options.Value;

    public async Task<PagedResult<ScenarioListItemResponse>> SearchAsync(ScenarioQueryRequest request, CancellationToken ct = default)
    {
        ValidateQuery(request);
        var tags = (request.Tag ?? []).Select(TagNames.Normalize).Distinct().ToList();

        var page = Math.Clamp(request.Page, 1, 100_000);   // chặn trần để (page - 1) * pageSize không tràn int
        var pageSize = Math.Clamp(request.PageSize ?? _options.DefaultPageSize, 1, _options.MaxPageSize);

        var criteria = new ScenarioSearchCriteria(
            Keyword: request.Q,
            Category: request.Category,
            Status: request.Status,
            MapName: request.Map,
            WeatherPreset: request.Weather,
            EventType: request.EventType,
            HasDraft: request.HasDraft,
            Tags: tags,
            Page: page,
            PageSize: pageSize);

        var data = await uow.Scenarios.SearchAsync(criteria, ct);
        var items = data.Items.Select(x => x.ToListItemResponse()).ToList();
        return new PagedResult<ScenarioListItemResponse>(items, page, pageSize, data.TotalCount);
    }

    public async Task<ScenarioDetailResponse> GetDetailAsync(Guid scenarioId, CancellationToken ct = default)
    {
        var scenario = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        ScenarioVersion? latest = scenario.LatestVersionId is { } versionId
            ? await uow.ScenarioVersions.GetReadOnlyAsync(versionId, ct)
            : null;

        var hasDraft = await uow.ScenarioDrafts.GetReadOnlyAsync(scenarioId, ct) is not null;
        return scenario.ToDetailResponse(latest, hasDraft, await uow.Tags.ListNamesAsync(scenarioId, ct));
    }

    public async Task<ScenarioDetailResponse> CreateAsync(CreateScenarioRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var category = request.Category?.Trim() ?? string.Empty;

        var errors = new Dictionary<string, string[]>();
        ValidateName(name, errors, required: true);
        ValidateDescription(description, errors);
        ValidateCategory(category, errors, required: true);
        var template = string.IsNullOrWhiteSpace(request.Template) ? null : request.Template.Trim();
        var templateJson = template is null ? null : ScenarioContentTemplate.LoadTemplate(template);
        if (template is not null && templateJson is null)
            errors["template"] = [$"Unknown template '{template}'."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var scenario = new Scenario
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Category = category,
            Status = ScenarioStatuses.Active,
            OriginType = ScenarioOriginTypes.Manual,   // origin_version_id phải NULL (ck_scenarios_origin_parent)
            LatestVersionNumber = 0,
            CreatedBy = currentUser.UserId
        };

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.Scenarios.Add(scenario);
            uow.ScenarioDrafts.Add(new ScenarioDraft
            {
                ScenarioId = scenario.Id,
                Content = templateJson is null
                    ? ScenarioContentTemplate.Create(name, category)
                    : ScenarioContentTemplate.FromTemplate(templateJson, name, category),
                UpdatedBy = currentUser.UserId
            });
            uow.AuditLogs.Write(AuditEntityTypes.Scenario, scenario.Id, AuditActions.Create, currentUser.UserId,
                new { name, category, template });

            await uow.SaveChangesAsync(token);
            return scenario.ToDetailResponse(latest: null, hasDraft: true, tags: []);
        }, ct);
    }

    public async Task<ScenarioDetailResponse> UpdateProfileAsync(Guid scenarioId, UpdateScenarioRequest request, CancellationToken ct = default)
    {
        var scenario = await uow.Scenarios.FindAsync([scenarioId], ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        if (scenario.Status == ScenarioStatuses.Archived)
            throw new BusinessRuleException(ErrorCodes.ScenarioArchived,
                "This scenario is archived. Restore it before editing.");

        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        var category = request.Category?.Trim();
        var description = request.Description?.Trim();

        if (name is not null) ValidateName(name, errors, required: false);
        if (category is not null) ValidateCategory(category, errors, required: false);
        if (description is not null) ValidateDescription(description, errors);
        if (errors.Count > 0) throw new RequestValidationException(errors);

        // Chỉ ghi các trường thật sự đổi; gửi lại đúng giá trị cũ thì không có dòng audit nào.
        var changes = new Dictionary<string, object?>();
        if (name is not null && name != scenario.Name)
            changes["name"] = new { from = scenario.Name, to = name };
        if (category is not null && category != scenario.Category)
            changes["category"] = new { from = scenario.Category, to = category };
        var newDescription = description is null ? scenario.Description : description.Length == 0 ? null : description;
        if (newDescription != scenario.Description)
            changes["description"] = new { from = scenario.Description, to = newDescription };

        if (name is not null) scenario.Name = name;
        if (category is not null) scenario.Category = category;
        scenario.Description = newDescription;

        // Hồ sơ không thuộc nội dung thực thi nên không sinh version; audit và thay đổi ghi trong cùng SaveChanges.
        if (changes.Count > 0)
            uow.AuditLogs.Write(AuditEntityTypes.Scenario, scenario.Id, AuditActions.UpdateProfile, currentUser.UserId,
                new { changes });
        await uow.SaveChangesAsync(ct);
        return await BuildDetailAsync(scenario, ct);
    }

    public async Task<ScenarioDetailResponse> ArchiveAsync(Guid scenarioId, CancellationToken ct = default)
    {
        var scenario = await uow.Scenarios.FindAsync([scenarioId], ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        if (scenario.Status == ScenarioStatuses.Archived)
            throw new ConflictException(ErrorCodes.ScenarioArchived, "This scenario is already archived.");

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            // CHECK ck_scenarios_archived: status = ARCHIVED <=> archived_at khác null
            scenario.Status = ScenarioStatuses.Archived;
            scenario.ArchivedBy = currentUser.UserId;
            scenario.ArchivedAt = DateTimeOffset.UtcNow;

            uow.AuditLogs.Write(AuditEntityTypes.Scenario, scenarioId, AuditActions.Archive, currentUser.UserId);
            await uow.SaveChangesAsync(token);
            return await BuildDetailAsync(scenario, token);
        }, ct);
    }

    public async Task<ScenarioDetailResponse> RestoreAsync(Guid scenarioId, CancellationToken ct = default)
    {
        var scenario = await uow.Scenarios.FindAsync([scenarioId], ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        if (scenario.Status != ScenarioStatuses.Archived)
            throw new ConflictException(ErrorCodes.ScenarioNotArchived, "This scenario is not archived.");

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            scenario.Status = ScenarioStatuses.Active;
            scenario.ArchivedBy = null;
            scenario.ArchivedAt = null;

            uow.AuditLogs.Write(AuditEntityTypes.Scenario, scenarioId, AuditActions.Restore, currentUser.UserId);
            await uow.SaveChangesAsync(token);
            return await BuildDetailAsync(scenario, token);
        }, ct);
    }

    public async Task<ScenarioDetailResponse> CloneAsync(Guid scenarioId, CloneScenarioRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var errors = new Dictionary<string, string[]>();
        ValidateName(name, errors, required: true);
        ValidateDescription(description, errors);
        if (request.VersionNumber is { } number && number < 1)
            errors["versionNumber"] = ["The version number must be at least 1"];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var source = await uow.Scenarios.GetReadOnlyAsync(scenarioId, ct)
            ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {scenarioId} was not found.");

        var sourceVersion = request.VersionNumber is { } versionNumber
            ? await uow.ScenarioVersions.GetByNumberAsync(scenarioId, versionNumber, ct)
              ?? throw new NotFoundException(ErrorCodes.VersionNotFound,
                  $"Scenario {source.Code} has no version {versionNumber}.")
            : source.LatestVersionId is { } latestId
                ? await uow.ScenarioVersions.GetReadOnlyAsync(latestId, ct)
                : null;

        if (sourceVersion is null)
            throw new BusinessRuleException(ErrorCodes.NoVersionToClone,
                "The source scenario has no version to clone. Publish a version first.");

        var clone = new Scenario
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description ?? source.Description,
            Category = source.Category,
            Status = ScenarioStatuses.Active,
            // CHECK ck_scenarios_origin_parent: clone bắt buộc có origin_version_id
            OriginType = ScenarioOriginTypes.Clone,
            OriginVersionId = sourceVersion.Id,
            LatestVersionNumber = 0,
            CreatedBy = currentUser.UserId
        };

        // Bản sao kế thừa tag của scenario nguồn (tag ở mức scenario, không nằm trong version).
        var tags = await uow.Tags.ListNamesAsync(scenarioId, ct);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.Scenarios.Add(clone);
            await uow.Tags.AttachAsync(clone.Id, tags, token);
            uow.ScenarioDrafts.Add(new ScenarioDraft
            {
                ScenarioId = clone.Id,
                Content = BuildCloneContent(sourceVersion, name, clone.Description),
                UpdatedBy = currentUser.UserId
            });
            uow.AuditLogs.Write(AuditEntityTypes.Scenario, clone.Id, AuditActions.Clone, currentUser.UserId,
                new { source_scenario_id = scenarioId, source_version_id = sourceVersion.Id, source_version_number = sourceVersion.VersionNumber });

            await uow.SaveChangesAsync(token);
            return clone.ToDetailResponse(latest: null, hasDraft: true, tags);
        }, ct);
    }

    /// <summary>
    /// Nội dung bản nháp của bản sao: giữ nguyên cấu hình, chỉ đổi khối metadata và source
    /// để giữ vết nguồn gốc (source.type = clone, parent_version_id).
    /// </summary>
    private static string BuildCloneContent(ScenarioVersion sourceVersion, string name, string? description)
    {
        var content = JsonNode.Parse(sourceVersion.Content) as JsonObject ?? new JsonObject();

        var metadata = content["metadata"] as JsonObject ?? new JsonObject();
        metadata["name"] = name;
        if (description is null) metadata.Remove("description");
        else metadata["description"] = description;
        content["metadata"] = metadata;

        content["source"] = new JsonObject
        {
            ["type"] = ScenarioOriginTypes.Clone,
            ["parent_version_id"] = sourceVersion.Id.ToString()
        };

        return content.ToJsonString();
    }

    private async Task<ScenarioDetailResponse> BuildDetailAsync(Scenario scenario, CancellationToken ct)
    {
        ScenarioVersion? latest = scenario.LatestVersionId is { } versionId
            ? await uow.ScenarioVersions.GetReadOnlyAsync(versionId, ct)
            : null;
        var hasDraft = await uow.ScenarioDrafts.GetReadOnlyAsync(scenario.Id, ct) is not null;
        return scenario.ToDetailResponse(latest, hasDraft, await uow.Tags.ListNamesAsync(scenario.Id, ct));
    }

    private static void ValidateName(string name, IDictionary<string, string[]> errors, bool required)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (required) errors["name"] = ["A scenario name is required"];
            else errors["name"] = ["The name cannot be empty"];
            return;
        }
        if (name.Length is < 3 or > 120) errors["name"] = ["The name must be between 3 and 120 characters"];
    }

    private static void ValidateDescription(string? description, IDictionary<string, string[]> errors)
    {
        if (description is { Length: > 2000 }) errors["description"] = ["The description must be at most 2,000 characters"];
    }

    private static void ValidateCategory(string category, IDictionary<string, string[]> errors, bool required)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            errors["category"] = required ? ["A category is required"] : ["The category cannot be empty"];
            return;
        }
        if (!ScenarioCatalog.Categories.Contains(category))
            errors["category"] = [$"Invalid value. Allowed values: {string.Join(", ", ScenarioCatalog.Categories)}"];
    }

    /// <summary>Kiểm tra giá trị lọc hợp lệ (tránh query vô nghĩa, trả lỗi rõ ràng cho frontend).</summary>
    private static void ValidateQuery(ScenarioQueryRequest r)
    {
        var errors = new Dictionary<string, string[]>();

        if (!string.IsNullOrWhiteSpace(r.Category) && !ScenarioCatalog.Categories.Contains(r.Category))
            errors["category"] = [$"Invalid value. Allowed values: {string.Join(", ", ScenarioCatalog.Categories)}"];

        if (!string.IsNullOrWhiteSpace(r.Weather) && !ScenarioCatalog.WeatherPresets.Contains(r.Weather))
            errors["weather"] = [$"Invalid value. Allowed values: {string.Join(", ", ScenarioCatalog.WeatherPresets)}"];

        if (!string.IsNullOrWhiteSpace(r.EventType) && !ScenarioCatalog.EventTypes.Contains(r.EventType))
            errors["eventType"] = [$"Invalid value. Allowed values: {string.Join(", ", ScenarioCatalog.EventTypes)}"];

        if (!string.IsNullOrWhiteSpace(r.Status) && r.Status is not (ScenarioStatuses.Active or ScenarioStatuses.Archived))
            errors["status"] = [$"Only {ScenarioStatuses.Active} or {ScenarioStatuses.Archived} is allowed"];

        if (r.Q is { Length: > 100 })
            errors["q"] = ["Search text must be at most 100 characters"];

        if (r.Tag is { } tags && (tags.Length > TagNames.MaxPerScenario || tags.Any(t => !TagNames.IsValid(TagNames.Normalize(t ?? "")))))
            errors["tag"] = [$"Use up to {TagNames.MaxPerScenario} valid tag names (lowercase letters, digits, '-' or '_')."];

        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
