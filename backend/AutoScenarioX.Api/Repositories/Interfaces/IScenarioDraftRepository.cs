using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Repositories.Interfaces;

public interface IScenarioDraftRepository : IGenericRepository<ScenarioDraft>
{
    Task<ScenarioDraft?> GetReadOnlyAsync(Guid scenarioId, CancellationToken ct = default);

    /// <summary>Lấy bản nháp CÓ tracking để sửa (lưu nháp, gán validation report).</summary>
    Task<ScenarioDraft?> GetForUpdateAsync(Guid scenarioId, CancellationToken ct = default);
}
