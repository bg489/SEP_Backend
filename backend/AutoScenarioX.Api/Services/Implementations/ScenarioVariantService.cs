using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.DTOs.Scenarios.Responses;
using AutoScenarioX.Api.Mappings;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Services.Variants;
using AutoScenarioX.Api.Validators.ScenarioContent;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class ScenarioVariantService(
    IUnitOfWork uow,
    IScenarioValidator validator,
    ICanonicalJsonHasher hasher,
    ICurrentUser currentUser,
    IOptions<ScenarioOptions> options) : IScenarioVariantService
{
    public async Task<VariantBatchResponse> GenerateAsync(
        Guid scenarioId, int versionNumber, GenerateVariantsRequest request, CancellationToken ct = default)
    {
        var source = await LoadScenarioAsync(scenarioId, ct);
        EnsureActive(source);
        var version = await LoadVersionAsync(scenarioId, versionNumber, ct);
        EnsureReady(version);
        var prefix = ResolvePrefix(request.NamePrefix, source.Name);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > 1000 })
            throw new RequestValidationException(new Dictionary<string, string[]> { ["note"] = ["Notes must be at most 1,000 characters."] });

        var original = JsonNode.Parse(version.Content)!.AsObject();
        var strategy = request.Strategy ?? VariantStrategies.Cartesian;
        var combinations = ParameterGridExpander.Expand(
            request.ParameterGrid, ScenarioContentReader.Parameters(original), options.Value.MaxVariantsPerBatch, strategy);
        var batch = new VariantBatch
        {
            Id = Guid.NewGuid(), BaseVersionId = version.Id,
            ParameterGrid = JsonSerializer.Serialize(request.ParameterGrid), Strategy = strategy,
            RequestedCount = combinations.Count, GeneratedCount = combinations.Count,
            Note = note, CreatedBy = currentUser.UserId
        };

        var maps = await uow.MapMetadata.ListActiveAsync(ct);
        var mapName = ScenarioContentReader.AsString(original["map"]?["name"]);
        var map = maps.FirstOrDefault(m => m.MapName == mapName);
        var scenarios = new List<Scenario>();
        var reports = new List<ValidationReport>();

        for (var i = 0; i < combinations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var name = $"{prefix} [variant {i + 1:D3}]";
            var content = BuildContent(original, version.Id, source, name, combinations[i]);
            var result = await ValidateAsync(content, map, maps.Count > 0, ct);
            var scenario = new Scenario
            {
                Id = Guid.NewGuid(), Name = name, Description = source.Description, Category = source.Category,
                OriginType = ScenarioOriginTypes.Variant, OriginVersionId = version.Id,
                VariantBatchId = batch.Id, CreatedBy = currentUser.UserId
            };
            var report = new ValidationReport
            {
                Id = Guid.NewGuid(), ScenarioId = scenario.Id, ContentHash = hasher.ComputeHash(content),
                IsValid = result.IsValid, StoppedAt = result.StoppedAt?.ToDbValue(),
                Errors = result.Errors.ToJsonArray(), Warnings = result.Warnings.ToJsonArray(),
                SchemaVersion = version.SchemaVersion, ValidatorVersion = ValidatorVersion.Current,
                MapMetadataId = map?.Id, CreatedBy = currentUser.UserId
            };
            if (!result.IsValid)
                throw new VariantValidationException(i + 1, JsonSerializer.SerializeToElement(combinations[i]),
                    new VariantValidationFailureResponse(result.StoppedAt?.ToDbValue(),
                        result.Errors.Select(e => new ValidationIssueResponse(e.Tier.ToDbValue(), e.Path, e.Message)).ToList(),
                        result.Warnings.Select(e => new ValidationIssueResponse(e.Tier.ToDbValue(), e.Path, e.Message)).ToList()));

            scenario.Draft = new ScenarioDraft
            {
                ScenarioId = scenario.Id, Content = CanonicalJsonSerializer.Serialize(content),
                LastValidationReportId = report.Id, UpdatedBy = currentUser.UserId
                // BaseVersionId thuộc scenario mới nên để null; nguồn gốc nằm ở origin/parent_version_id.
            };
            scenarios.Add(scenario);
            reports.Add(report);
        }

        // Biến thể kế thừa tag của scenario gốc.
        var tags = await uow.Tags.ListNamesAsync(scenarioId, ct);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            // Kiểm tra lại sau khi validate để không sinh thêm từ scenario vừa bị archive.
            await uow.Scenarios.LockForUpdateAsync(scenarioId, token);
            EnsureActive(await LoadScenarioAsync(scenarioId, token));
            EnsureReady(await LoadVersionAsync(scenarioId, versionNumber, token));

            uow.VariantBatches.Add(batch);
            foreach (var report in reports) uow.ValidationReports.Add(report);
            foreach (var scenario in scenarios)
            {
                uow.Scenarios.Add(scenario);
                await uow.Tags.AttachAsync(scenario.Id, tags, token);
                uow.AuditLogs.Write(AuditEntityTypes.Scenario, scenario.Id, AuditActions.Create, currentUser.UserId,
                    new { origin_type = ScenarioOriginTypes.Variant, variant_batch_id = batch.Id, parent_version_id = version.Id });
            }
            uow.AuditLogs.Write(AuditEntityTypes.VariantBatch, batch.Id, AuditActions.GenerateVariants, currentUser.UserId,
                new { base_version_id = version.Id, strategy, requested_count = batch.RequestedCount,
                    generated_count = batch.GeneratedCount, parameter_grid = request.ParameterGrid });
            await uow.SaveChangesAsync(token);
            return new VariantBatchResponse(batch.ToSummaryResponse(), scenarios.Select(s => s.ToVariantResponse()).ToList());
        }, ct);
    }

    public async Task<IReadOnlyList<VariantBatchSummaryResponse>> ListAsync(
        Guid scenarioId, int versionNumber, CancellationToken ct = default)
    {
        await LoadScenarioAsync(scenarioId, ct);
        var version = await LoadVersionAsync(scenarioId, versionNumber, ct);
        var batches = await uow.VariantBatches.ListByVersionAsync(version.Id, ct);
        return batches.Select(b => b.ToSummaryResponse()).ToList();
    }

    public async Task<VariantBatchResponse> GetAsync(
        Guid scenarioId, int versionNumber, Guid batchId, CancellationToken ct = default)
    {
        await LoadScenarioAsync(scenarioId, ct);
        var version = await LoadVersionAsync(scenarioId, versionNumber, ct);
        var batch = await uow.VariantBatches.GetReadOnlyAsync(batchId, version.Id, ct)
            ?? throw new NotFoundException(ErrorCodes.VariantBatchNotFound, "The batch was not found for this version.");
        var scenarios = await uow.VariantBatches.ListScenariosAsync(batchId, ct);
        return new VariantBatchResponse(batch.ToSummaryResponse(), scenarios.Select(s => s.ToVariantResponse()).ToList());
    }

    private async Task<ScenarioValidationResult> ValidateAsync(
        JsonObject content, MapMetadata? map, bool metadataAvailable, CancellationToken ct)
    {
        ScenarioValidationContext Context(JsonObject document) => new()
        {
            Raw = JsonSerializer.SerializeToElement(document), Normalized = document,
            MapName = ScenarioContentReader.AsString(document["map"]?["name"]),
            Map = map, MapMetadataAvailable = metadataAvailable
        };

        var result = await validator.ValidateAsync(Context(content), ct);
        if (!result.IsValid) return result;

        // Kiểm tra cả miền của trường sử dụng $param (vd speed_ms <= 5, số NPC nguyên).
        // Bản lưu vẫn giữ $param và definitions; chỉ bản tạm dùng cho kiểm tra được resolve.
        var resolved = (JsonObject)content.DeepClone();
        ScenarioContentReader.ResolveParamReferences(resolved, ScenarioContentReader.Parameters(content));
        var resolvedResult = await validator.ValidateAsync(Context(resolved), ct);
        return resolvedResult with { Warnings = result.Warnings.Concat(resolvedResult.Warnings).Distinct().ToList() };
    }

    private static JsonObject BuildContent(JsonObject original, Guid parentVersionId, Scenario source, string name, JsonObject values)
    {
        var content = (JsonObject)original.DeepClone();
        foreach (var (parameter, value) in values)
        {
            // Trục thời tiết đổi preset, giữ các giá trị ghi đè (mưa, sương…) nếu scenario gốc có.
            if (parameter == ParameterGridExpander.WeatherAxis)
                content["weather"]!["preset"] = value!.DeepClone();
            else
                content["parameters"]![parameter]!["value"] = value!.DeepClone();
        }
        var metadata = content["metadata"]!.AsObject();
        metadata["name"] = name;
        metadata["category"] = source.Category;
        if (source.Description is null) metadata.Remove("description");
        else metadata["description"] = source.Description;
        content["source"] = new JsonObject
        {
            ["type"] = ScenarioOriginTypes.Variant, ["parent_version_id"] = parentVersionId.ToString()
        };
        return ScenarioContentNormalizer.Normalize(JsonSerializer.SerializeToElement(content));
    }

    private static string ResolvePrefix(string? requested, string sourceName)
    {
        if (requested is null) return sourceName[..Math.Min(sourceName.Length, 100)];
        var prefix = requested.Trim();
        if (prefix.Length is < 1 or > 100)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["namePrefix"] = ["The name prefix must be between 1 and 100 characters."] });
        return prefix;
    }

    private async Task<Scenario> LoadScenarioAsync(Guid id, CancellationToken ct)
        => await uow.Scenarios.GetReadOnlyAsync(id, ct)
           ?? throw new NotFoundException(ErrorCodes.ScenarioNotFound, $"Scenario {id} was not found.");

    private async Task<ScenarioVersion> LoadVersionAsync(Guid id, int number, CancellationToken ct)
        => await uow.ScenarioVersions.GetByNumberAsync(id, number, ct)
           ?? throw new NotFoundException(ErrorCodes.VersionNotFound, $"Version {number} was not found for scenario {id}.");

    private static void EnsureActive(Scenario scenario)
    {
        if (scenario.Status == ScenarioStatuses.Archived)
            throw new BusinessRuleException(ErrorCodes.ScenarioArchived, "Restore the scenario before generating variants.");
    }

    private static void EnsureReady(ScenarioVersion version)
    {
        if (version.Status != VersionStatuses.Ready)
            throw new BusinessRuleException(ErrorCodes.VariantBaseNotReady, "Variants can only be generated from READY versions.");
    }
}
