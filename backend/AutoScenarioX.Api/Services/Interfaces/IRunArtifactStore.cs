namespace AutoScenarioX.Api.Services.Interfaces;

public interface IRunArtifactStore
{
    bool IsCloud { get; }
    Task UploadAsync(string relative, string fullPath, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string relative, CancellationToken ct = default);
}
