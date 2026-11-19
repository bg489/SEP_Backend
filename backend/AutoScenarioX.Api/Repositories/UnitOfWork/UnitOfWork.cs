using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoScenarioX.Api.Repositories.UnitOfWork;

public sealed class UnitOfWork(
    AsxDbContext db,
    IScenarioRepository scenarios,
    IScenarioVersionRepository scenarioVersions,
    IScenarioDraftRepository scenarioDrafts,
    IMapMetadataRepository mapMetadata,
    IValidationReportRepository validationReports,
    IImportRecordRepository importRecords,
    IVariantBatchRepository variantBatches,
    IAuditLogRepository auditLogs,
    IAppUserRepository appUsers,
    ICampaignRepository campaigns,
    IOracleConfigRepository oracleConfigs,
    IRunRepository runs,
    IFailureAnalysisRepository failureAnalyses,
    ITagRepository tags) : IUnitOfWork
{
    public IScenarioRepository Scenarios { get; } = scenarios;
    public IScenarioVersionRepository ScenarioVersions { get; } = scenarioVersions;
    public IScenarioDraftRepository ScenarioDrafts { get; } = scenarioDrafts;
    public IMapMetadataRepository MapMetadata { get; } = mapMetadata;
    public IValidationReportRepository ValidationReports { get; } = validationReports;
    public IImportRecordRepository ImportRecords { get; } = importRecords;
    public IVariantBatchRepository VariantBatches { get; } = variantBatches;
    public IAuditLogRepository AuditLogs { get; } = auditLogs;
    public IAppUserRepository AppUsers { get; } = appUsers;
    public ICampaignRepository Campaigns { get; } = campaigns;
    public IOracleConfigRepository OracleConfigs { get; } = oracleConfigs;
    public IRunRepository Runs { get; } = runs;
    public IFailureAnalysisRepository FailureAnalyses { get; } = failureAnalyses;
    public ITagRepository Tags { get; } = tags;

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Translate(ex) is { } appEx)
        {
            throw appEx;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work(ct);
            await tx.CommitAsync(ct);   // ràng buộc DEFERRED được kiểm tra tại đây
            return result;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            if (ex is not AppException && Translate(ex) is { } appEx) throw appEx;
            throw;
        }
    }

    public async Task<DatabaseStatus> GetDatabaseStatusAsync(CancellationToken ct = default)
    {
        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name::text AS \"Value\" FROM information_schema.tables WHERE table_schema = 'asx' AND table_type = 'BASE TABLE'")
            .ToListAsync(ct);
        var version = await db.Database
            .SqlQuery<string>($"SELECT version() AS \"Value\"")
            .SingleAsync(ct);
        return new DatabaseStatus(tables, version);
    }

    /// <summary>
    /// Dịch lỗi PostgreSQL sang exception nghiệp vụ. Trả null nếu không phải lỗi dữ liệu đã biết.
    /// Mã SQLSTATE: https://www.postgresql.org/docs/current/errcodes-appendix.html
    /// </summary>
    private static AppException? Translate(Exception ex)
    {
        if (ex is DbUpdateConcurrencyException)
            return new ConflictException(ErrorCodes.ConcurrencyConflict,
                "This data has changed. Reload it and try again.");

        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        if (pg is null) return null;

        return pg.SqlState switch
        {
            // 23505 unique_violation
            PostgresErrorCodes.UniqueViolation when pg.ConstraintName == "uq_versions_hash" =>
                new ConflictException(ErrorCodes.DuplicateVersion, "This content is identical to an existing version."),
            PostgresErrorCodes.UniqueViolation =>
                new ConflictException(ErrorCodes.DuplicateData, $"Duplicate data ({pg.ConstraintName})."),
            // 23503 foreign_key_violation
            PostgresErrorCodes.ForeignKeyViolation =>
                new BusinessRuleException(ErrorCodes.ReferenceNotFound, $"Referenced data does not exist ({pg.ConstraintName})."),
            // 23514 check_violation (gồm lỗi từ trigger kiểm tra validate trước khi publish)
            PostgresErrorCodes.CheckViolation =>
                new BusinessRuleException(ErrorCodes.ConstraintViolation,
                    "This change violates a data constraint. Review the configuration and validate again."),
            // 23000 integrity_constraint_violation (trigger bảo vệ version bất biến)
            PostgresErrorCodes.IntegrityConstraintViolation =>
                new BusinessRuleException(ErrorCodes.VersionImmutable,
                    "Published versions are immutable. Create a draft to make changes."),
            _ => null
        };
    }
}
