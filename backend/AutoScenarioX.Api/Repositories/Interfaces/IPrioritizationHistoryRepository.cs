namespace AutoScenarioX.Api.Repositories.Interfaces;

public sealed record VersionFailureHistory(
    Guid VersionId, int EvaluatedRuns, int FailedRuns, double? MeanDurationMinutes,
    IReadOnlyList<string> FaultIds);

public interface IPrioritizationHistoryRepository
{
    Task<IReadOnlyDictionary<Guid, VersionFailureHistory>> ReadAsync(IReadOnlyList<Guid> versionIds, CancellationToken ct = default);
}
