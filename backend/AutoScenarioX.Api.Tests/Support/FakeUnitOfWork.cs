using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Repositories.Interfaces;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Support;

/// <summary>
/// IUnitOfWork cho unit test service: repository là NSubstitute, transaction chạy thẳng khối công việc,
/// đếm số lần SaveChanges để kiểm chứng "lỗi thì không ghi gì".
/// </summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public IScenarioRepository Scenarios { get; } = Substitute.For<IScenarioRepository>();
    public IScenarioVersionRepository ScenarioVersions { get; } = Substitute.For<IScenarioVersionRepository>();
    public IScenarioDraftRepository ScenarioDrafts { get; } = Substitute.For<IScenarioDraftRepository>();
    public IMapMetadataRepository MapMetadata { get; } = Substitute.For<IMapMetadataRepository>();
    public IValidationReportRepository ValidationReports { get; } = Substitute.For<IValidationReportRepository>();
    public IImportRecordRepository ImportRecords { get; } = Substitute.For<IImportRecordRepository>();
    public IVariantBatchRepository VariantBatches { get; } = Substitute.For<IVariantBatchRepository>();
    public IAuditLogRepository AuditLogs { get; } = Substitute.For<IAuditLogRepository>();
    public IAppUserRepository AppUsers { get; } = Substitute.For<IAppUserRepository>();
    public ICampaignRepository Campaigns { get; } = Substitute.For<ICampaignRepository>();
    public IOracleConfigRepository OracleConfigs { get; } = Substitute.For<IOracleConfigRepository>();
    public IRunRepository Runs { get; } = Substitute.For<IRunRepository>();
    public IFailureAnalysisRepository FailureAnalyses { get; } = Substitute.For<IFailureAnalysisRepository>();
    public ITagRepository Tags { get; } = Substitute.For<ITagRepository>();

    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
        => work(ct);

    /// <summary>Tên bảng health check thấy trong schema asx.</summary>
    public IReadOnlyList<string> AsxTables { get; set; } = [];

    public Task<DatabaseStatus> GetDatabaseStatusAsync(CancellationToken ct = default)
        => Task.FromResult(new DatabaseStatus(AsxTables, "fake"));

    public static ICurrentUser User(Guid? id = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(id ?? Guid.Parse("d0000000-0000-0000-0000-000000000001"));
        user.IsAuthenticated.Returns(true);
        user.Role.Returns(AppRoles.ScenarioEngineer);
        return user;
    }
}
