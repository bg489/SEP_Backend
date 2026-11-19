using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface ITagRepository : IGenericRepository<Tag>
{
    /// <summary>Các tag đang được ít nhất một scenario dùng, sắp theo tên.</summary>
    Task<IReadOnlyList<TagUsage>> ListUsageAsync(CancellationToken ct = default);

    /// <summary>Tên tag của một scenario, sắp theo tên.</summary>
    Task<IReadOnlyList<string>> ListNamesAsync(Guid scenarioId, CancellationToken ct = default);

    /// <summary>
    /// Gắn tag cho scenario (kể cả scenario mới chưa lưu); tạo dòng asx.tags còn thiếu.
    /// Chỉ thêm vào change tracker, ghi DB khi UnitOfWork.SaveChangesAsync.
    /// </summary>
    Task AttachAsync(Guid scenarioId, IReadOnlyCollection<string> names, CancellationToken ct = default);

    /// <summary>Gỡ tag khỏi scenario. Dòng asx.tags giữ lại để dùng tiếp.</summary>
    Task DetachAsync(Guid scenarioId, IReadOnlyCollection<string> names, CancellationToken ct = default);
}

public sealed record TagUsage(string Name, int ScenarioCount, int ActiveScenarioCount);
