using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Json;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Luật publish/deprecate (CLAUDE.md "Quy trình Publish") với repository giả.</summary>
public class ScenarioVersionServiceTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly CanonicalJsonHasher _hasher = new();
    private readonly ScenarioVersionService _service;
    private readonly Scenario _scenario;
    private readonly ScenarioDraft _draft;
    private readonly ValidationReport _report;
    private readonly string _hash;

    public ScenarioVersionServiceTests()
    {
        _service = new ScenarioVersionService(_uow, _hasher, FakeUnitOfWork.User());

        var content = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        content["source"] = new JsonObject { ["type"] = "manual" };
        _hash = _hasher.ComputeHash(ScenarioContentNormalizer.Normalize(content));

        _scenario = new Scenario
        {
            Id = Guid.NewGuid(), Code = "SCN-09999", Name = "Unit", Category = "pedestrian",
            Status = ScenarioStatuses.Active, OriginType = ScenarioOriginTypes.Manual,
            LatestVersionId = Guid.NewGuid(), LatestVersionNumber = 2
        };
        _report = new ValidationReport
        {
            Id = Guid.NewGuid(), ScenarioId = _scenario.Id, ContentHash = _hash, IsValid = true, SchemaVersion = "1.0"
        };
        _draft = new ScenarioDraft
        {
            ScenarioId = _scenario.Id, Content = content.ToJsonString(), LastValidationReportId = _report.Id, RowVersion = 4
        };

        _uow.Scenarios.FindAsync(Arg.Is<object[]>(k => (Guid)k[0] == _scenario.Id), Arg.Any<CancellationToken>()).Returns(_scenario);
        _uow.ScenarioDrafts.GetForUpdateAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(_draft);
        _uow.ValidationReports.GetReadOnlyAsync(_report.Id, Arg.Any<CancellationToken>()).Returns(_report);
    }

    private Task Publish(string? note = null) => _service.PublishAsync(_scenario.Id, new PublishVersionRequest { ChangeNote = note });

    private async Task<T> AssertRejected<T>(Task action) where T : Exception
    {
        var error = await Assert.ThrowsAsync<T>(() => action);
        Assert.Equal(0, _uow.SaveCount);
        _uow.ScenarioVersions.DidNotReceive().Add(Arg.Any<ScenarioVersion>());
        _uow.ScenarioDrafts.DidNotReceive().Remove(Arg.Any<ScenarioDraft>());
        return error;
    }

    [Fact]
    public async Task Publish_creates_next_version_moves_latest_pointer_and_removes_draft()
    {
        var previousLatest = _scenario.LatestVersionId;

        var result = await _service.PublishAsync(_scenario.Id, new PublishVersionRequest { ChangeNote = "  first  " });

        Assert.Equal(3, result.Summary.VersionNumber);
        Assert.Equal(VersionStatuses.Ready, result.Summary.Status);
        Assert.Equal(_hash, result.Summary.ContentHash);
        Assert.Equal("first", result.Summary.ChangeNote);
        Assert.Equal(result.Summary.Id, _scenario.LatestVersionId);
        Assert.NotEqual(previousLatest, _scenario.LatestVersionId);
        Assert.Equal(3, _scenario.LatestVersionNumber);
        Assert.Null(result.ParentVersionId);   // manual: không gắn version cha

        _uow.ScenarioVersions.Received(1).Add(Arg.Is<ScenarioVersion>(v =>
            v.VersionNumber == 3 && v.ContentHash == _hash && v.ValidationReportId == _report.Id && v.SourceType == "manual"));
        _uow.ScenarioDrafts.Received(1).Remove(_draft);
        _uow.AuditLogs.Received(1).Write(AuditEntityTypes.Version, result.Summary.Id, AuditActions.Publish,
            Arg.Any<Guid>(), Arg.Any<object?>());
        await _uow.Scenarios.Received(1).LockForUpdateAsync(_scenario.Id, Arg.Any<CancellationToken>());
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Stored_content_is_canonical_and_hashes_to_content_hash()
    {
        var result = await _service.PublishAsync(_scenario.Id, new PublishVersionRequest());
        var stored = JsonNode.Parse(result.Content!.Value.GetRawText())!.AsObject();
        Assert.Equal(_hash, _hasher.ComputeHash(stored));
    }

    [Fact]
    public async Task Archived_scenario_cannot_publish()
    {
        _scenario.Status = ScenarioStatuses.Archived;
        Assert.Equal(ErrorCodes.ScenarioArchived, (await AssertRejected<BusinessRuleException>(Publish())).Code);
    }

    [Fact]
    public async Task Missing_draft_is_not_found()
    {
        _uow.ScenarioDrafts.GetForUpdateAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns((ScenarioDraft?)null);
        Assert.Equal(ErrorCodes.DraftNotFound, (await AssertRejected<NotFoundException>(Publish())).Code);
    }

    [Fact]
    public async Task Draft_must_be_validated()
    {
        _draft.LastValidationReportId = null;
        Assert.Equal(ErrorCodes.ValidationRequired, (await AssertRejected<BusinessRuleException>(Publish())).Code);
    }

    [Fact]
    public async Task Invalid_report_blocks_publish()
    {
        _report.IsValid = false;
        _report.StoppedAt = "business";
        var error = await AssertRejected<BusinessRuleException>(Publish());
        Assert.Equal(ErrorCodes.ValidationRequired, error.Code);
        Assert.Contains("business", error.Message);
    }

    [Fact]
    public async Task Content_changed_after_validation_blocks_publish()
    {
        _report.ContentHash = "sha256:" + new string('0', 64);
        Assert.Equal(ErrorCodes.ContentChangedSinceValidation, (await AssertRejected<BusinessRuleException>(Publish())).Code);
    }

    [Fact]
    public async Task Report_of_another_scenario_blocks_publish()
    {
        _report.ScenarioId = Guid.NewGuid();
        Assert.Equal(ErrorCodes.ContentChangedSinceValidation, (await AssertRejected<BusinessRuleException>(Publish())).Code);
    }

    [Fact]
    public async Task Duplicate_content_of_existing_version_is_conflict()
    {
        _uow.ScenarioVersions.FindByContentHashAsync(_scenario.Id, _hash, Arg.Any<CancellationToken>())
            .Returns(new ScenarioVersion { Id = Guid.NewGuid(), VersionNumber = 1, ContentHash = _hash, Content = "{}" });
        var error = await AssertRejected<ConflictException>(Publish());
        Assert.Equal(ErrorCodes.DuplicateVersion, error.Code);
        Assert.Contains("version 1", error.Message);
    }

    [Fact]
    public async Task Change_note_longer_than_1000_is_rejected()
    {
        var error = await AssertRejected<RequestValidationException>(Publish(new string('x', 1001)));
        Assert.True(error.Errors.ContainsKey("changeNote"));
    }

    [Fact]
    public async Task Variant_source_keeps_parent_version()
    {
        var parent = Guid.NewGuid();
        var content = JsonNode.Parse(_draft.Content)!.AsObject();
        content["source"] = new JsonObject { ["type"] = "variant", ["parent_version_id"] = parent.ToString() };
        _draft.Content = content.ToJsonString();
        _report.ContentHash = _hasher.ComputeHash(ScenarioContentNormalizer.Normalize(content));

        var result = await _service.PublishAsync(_scenario.Id, new PublishVersionRequest());

        Assert.Equal(parent, result.ParentVersionId);
        Assert.Equal("variant", result.Summary.SourceType);
    }

    [Fact]
    public async Task Deprecate_marks_version_and_writes_audit()
    {
        var version = new ScenarioVersion { Id = Guid.NewGuid(), ScenarioId = _scenario.Id, VersionNumber = 2, Status = VersionStatuses.Ready, Content = "{}", ContentHash = _hash };
        _uow.Scenarios.ExistsAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(true);
        _uow.ScenarioVersions.GetByNumberAsync(_scenario.Id, 2, Arg.Any<CancellationToken>()).Returns(version);
        _uow.ScenarioVersions.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>()).Returns(version);

        var result = await _service.DeprecateAsync(_scenario.Id, 2);

        Assert.Equal(VersionStatuses.Deprecated, result.Status);
        Assert.NotNull(version.DeprecatedAt);
        _uow.AuditLogs.Received(1).Write(AuditEntityTypes.Version, version.Id, AuditActions.Deprecate, Arg.Any<Guid>(), Arg.Any<object?>());
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Deprecating_twice_is_conflict()
    {
        var version = new ScenarioVersion { Id = Guid.NewGuid(), ScenarioId = _scenario.Id, VersionNumber = 1, Status = VersionStatuses.Deprecated, Content = "{}", ContentHash = _hash };
        _uow.Scenarios.ExistsAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(true);
        _uow.ScenarioVersions.GetByNumberAsync(_scenario.Id, 1, Arg.Any<CancellationToken>()).Returns(version);
        _uow.ScenarioVersions.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>()).Returns(version);

        var error = await Assert.ThrowsAsync<ConflictException>(() => _service.DeprecateAsync(_scenario.Id, 1));
        Assert.Equal(ErrorCodes.VersionAlreadyDeprecated, error.Code);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Diff_of_same_version_is_rejected()
    {
        _uow.Scenarios.ExistsAsync(_scenario.Id, Arg.Any<CancellationToken>()).Returns(true);
        await Assert.ThrowsAsync<RequestValidationException>(() => _service.DiffAsync(_scenario.Id, 1, 1));
    }
}
