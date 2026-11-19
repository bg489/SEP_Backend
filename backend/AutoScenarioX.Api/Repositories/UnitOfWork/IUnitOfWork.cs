using AutoScenarioX.Api.Repositories.Interfaces;

namespace AutoScenarioX.Api.Repositories.UnitOfWork;

/// <summary>
/// Gom các repository dùng chung một DbContext + quản lý lưu và transaction.
/// Service chỉ làm việc với DB qua interface này.
/// </summary>
public interface IUnitOfWork
{
    IScenarioRepository Scenarios { get; }
    IScenarioVersionRepository ScenarioVersions { get; }
    IScenarioDraftRepository ScenarioDrafts { get; }
    IMapMetadataRepository MapMetadata { get; }
    IValidationReportRepository ValidationReports { get; }
    IImportRecordRepository ImportRecords { get; }
    IVariantBatchRepository VariantBatches { get; }
    IAuditLogRepository AuditLogs { get; }
    IAppUserRepository AppUsers { get; }
    ICampaignRepository Campaigns { get; }
    IOracleConfigRepository OracleConfigs { get; }
    IRunRepository Runs { get; }
    IFailureAnalysisRepository FailureAnalyses { get; }
    ITagRepository Tags { get; }

    /// <summary>Lưu thay đổi. Lỗi PostgreSQL được dịch sang AppException.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Chạy một khối công việc trong transaction; lỗi thì rollback toàn bộ.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);

    /// <summary>Kiểm tra kết nối DB và liệt kê tên bảng trong schema asx (dùng cho health check).</summary>
    Task<DatabaseStatus> GetDatabaseStatusAsync(CancellationToken ct = default);
}

public sealed record DatabaseStatus(IReadOnlyList<string> AsxTables, string ServerVersion);
