using AutoScenarioX.Api.DTOs.Common;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Interfaces;

namespace AutoScenarioX.Api.Services.Implementations;

public sealed class SystemHealthService(IUnitOfWork uow) : ISystemHealthService
{
    private const int ExpectedAsxTables = 18;   // 11 bảng Flow 1 + 3 bảng Flow 2 + 4 bảng Flow 3 (migration 105)
    /// <summary>
    /// Bảng Flow 1 tạo trong database/migrations/. So theo tên vì schema asx dùng chung với flow khác
    /// (vd campaigns của Flow 2), nên đếm tổng số bảng sẽ báo nhầm.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredTables =
    [
        "app_users", "audit_logs", "import_records", "map_metadata", "scenario_drafts", "scenario_tags",
        "scenario_versions", "scenarios", "tags", "validation_reports", "variant_batches"
    ];

    public async Task<HealthResponse> CheckDatabaseAsync(CancellationToken ct = default)
    {
        var status = await uow.GetDatabaseStatusAsync(ct);
        var missing = RequiredTables.Except(status.AsxTables).ToList();
        return new HealthResponse(missing.Count == 0 ? "OK" : "SCHEMA_INCOMPLETE", missing, status.ServerVersion);
    }
}
