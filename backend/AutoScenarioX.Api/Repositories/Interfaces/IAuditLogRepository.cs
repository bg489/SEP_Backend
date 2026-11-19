using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Criteria;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IAuditLogRepository : IGenericRepository<AuditLog>
{
    /// <summary>
    /// Thêm một dòng nhật ký vào change tracker (chưa ghi DB - do SaveChangesAsync của UnitOfWork).
    /// entityType/action lấy từ AuditEntityTypes / AuditActions.
    /// </summary>
    void Write(string entityType, Guid entityId, string action, Guid actorId, object? details = null);

    /// <summary>
    /// Nhật ký liên quan tới một scenario, mới nhất trước: sự kiện của chính scenario và draft, của các version,
    /// file import và batch biến thể thuộc scenario, và sự kiện CLONE tạo scenario khác từ version của nó.
    /// </summary>
    Task<PagedData<AuditLog>> ListForScenarioAsync(Guid scenarioId, int page, int pageSize, CancellationToken ct = default);
}
