namespace AutoScenarioX.Api.Common.Options;

/// <summary>Section "Telemetry": nơi lưu file .jsonl.gz / .mp4 của Run.</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public string Directory { get; set; } = "telemetry-store";
    public int MaxUploadMb { get; set; } = 100;
    /// <summary>Số lần worker được re-queue một run sau INFRA_FAILED.</summary>
    public int MaxInfraRetries { get; set; } = 2;
    public int MaxVideoUploadMb { get; set; } = 200;
    /// <summary>Filesystem (default) or Supabase; Supabase uses a private bucket and server credentials only.</summary>
    public string StorageProvider { get; set; } = "Filesystem";
    public string? StorageProjectUrl { get; set; }
    public string StorageBucket { get; set; } = "run-artifacts";
    public string? StorageServiceKey { get; set; }
}
