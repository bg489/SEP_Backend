using System.Text.Json;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Giải nghĩa audit log thành timeline: số version, scenario liên quan, người thực hiện.</summary>
public class ScenarioHistoryServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ScenarioHistoryService _service;
    private static readonly Guid S = Guid.NewGuid(), Source = Guid.NewGuid(), Clone = Guid.NewGuid();
    private static readonly Guid V1 = Guid.NewGuid(), V2 = Guid.NewGuid(), SourceV3 = Guid.NewGuid(), Batch = Guid.NewGuid();
    private static readonly Guid Engineer = Guid.NewGuid(), Ghost = Guid.NewGuid();
    private long _id;

    public ScenarioHistoryServiceTests()
    {
        _service = new ScenarioHistoryService(_uow, Options.Create(new ScenarioOptions { MaxPageSize = 100 }));
        _uow.Scenarios.GetReadOnlyAsync(S, Arg.Any<CancellationToken>()).Returns(new Scenario { Id = S, Name = "S", Code = "SCN-00009" });
        _uow.VariantBatches.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new VariantBatch { Id = Batch, BaseVersionId = V2 }]);
        _uow.ScenarioVersions.ListRefsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new VersionRef(V1, S, 1), new VersionRef(V2, S, 2), new VersionRef(SourceV3, Source, 3)]);
        _uow.Scenarios.ListRefsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ScenarioRef(Source, "SCN-00001", "Source"), new ScenarioRef(Clone, "SCN-00042", "Copy")]);
        _uow.AppUsers.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new AppUser { Id = Engineer, DisplayName = "Scenario Engineer", Email = "engineer@example.test" }]);
    }

    private AuditLog Log(string type, Guid entity, string action, object? details = null, Guid? actor = null) => new()
    {
        Id = ++_id, EntityType = type, EntityId = entity, Action = action, ActorId = actor ?? Engineer,
        Details = details is null ? "{}" : JsonSerializer.Serialize(details), CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(_id)
    };

    private void Returns(params AuditLog[] logs)
        => _uow.AuditLogs.ListForScenarioAsync(S, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedData<AuditLog>(logs, logs.Length));

    [Fact]
    public async Task Version_numbers_come_from_the_version_the_event_concerns()
    {
        Returns(
            Log(AuditEntityTypes.Version, V2, AuditActions.Publish, new { version_number = 2 }),
            Log(AuditEntityTypes.Version, V1, AuditActions.Export, new { format = "yaml" }),
            Log(AuditEntityTypes.VariantBatch, Batch, AuditActions.GenerateVariants, new { generated_count = 6 }),
            Log(AuditEntityTypes.Draft, S, AuditActions.Create, new { base_version_number = 1 }),
            Log(AuditEntityTypes.Draft, S, AuditActions.UpdateDraft, new { row_version = 3 }));

        var items = (await _service.GetAsync(S, 1, null)).Items;

        Assert.Equal([2, 1, 2, 1, null], items.Select(i => i.VersionNumber));
        Assert.Equal(6, items[2].Details.GetProperty("generated_count").GetInt32());
        Assert.All(items, i => Assert.Null(i.RelatedScenario));
    }

    [Fact]
    public async Task Clones_point_to_the_other_scenario_in_both_directions()
    {
        Returns(
            Log(AuditEntityTypes.Scenario, S, AuditActions.Clone, new { source_scenario_id = Source, source_version_id = SourceV3, source_version_number = 3 }),
            Log(AuditEntityTypes.Scenario, Clone, AuditActions.Clone, new { source_scenario_id = S, source_version_id = V2, source_version_number = 2 }));

        var items = (await _service.GetAsync(S, 1, null)).Items;

        Assert.Equal(("SCN-00001", 3), (items[0].RelatedScenario!.Code, items[0].VersionNumber));   // clone từ nguồn
        Assert.Equal(("SCN-00042", 2), (items[1].RelatedScenario!.Code, items[1].VersionNumber));   // clone ra scenario khác
    }

    [Fact]
    public async Task Variant_creation_points_to_the_parent_scenario_and_version()
    {
        Returns(Log(AuditEntityTypes.Scenario, S, AuditActions.Create,
            new { origin_type = "variant", variant_batch_id = Batch, parent_version_id = SourceV3 }));

        var item = Assert.Single((await _service.GetAsync(S, 1, null)).Items);

        Assert.Equal("SCN-00001", item.RelatedScenario!.Code);
        Assert.Equal(3, item.VersionNumber);
    }

    [Fact]
    public async Task Actor_name_is_resolved_and_missing_users_are_labelled()
    {
        Returns(Log(AuditEntityTypes.Scenario, S, AuditActions.Archive), Log(AuditEntityTypes.Scenario, S, AuditActions.Restore, actor: Ghost));

        var items = (await _service.GetAsync(S, 1, null)).Items;

        Assert.Equal("Scenario Engineer", items[0].Actor.DisplayName);
        Assert.Equal(("Unknown user", Ghost), (items[1].Actor.DisplayName, items[1].Actor.Id));
    }

    [Theory]
    [InlineData(0, null, 1, 50)]
    [InlineData(3, 1000, 3, 100)]
    [InlineData(2, 0, 2, 1)]
    public async Task Paging_is_clamped(int page, int? pageSize, int expectedPage, int expectedSize)
    {
        Returns();
        var result = await _service.GetAsync(S, page, pageSize);
        Assert.Equal((expectedPage, expectedSize), (result.Page, result.PageSize));
        await _uow.AuditLogs.Received(1).ListForScenarioAsync(S, expectedPage, expectedSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_scenario_is_not_found()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(Guid.NewGuid(), 1, null));
        Assert.Equal(ErrorCodes.ScenarioNotFound, error.Code);
    }
}
