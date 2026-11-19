using System.Net;
using System.Net.Http.Headers;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Services.Implementations;

/// <summary>Optional private Supabase Storage adapter. Local files remain the configured default.</summary>
public sealed class RunArtifactStore(HttpClient http, IOptions<TelemetryOptions> options) : IRunArtifactStore
{
    private readonly TelemetryOptions _options = options.Value;
    public bool IsCloud => _options.StorageProvider.Equals("Supabase", StringComparison.OrdinalIgnoreCase);

    public async Task UploadAsync(string relative, string fullPath, string contentType, CancellationToken ct = default)
    {
        if (!IsCloud) return;
        using var request = Request(HttpMethod.Post, relative);
        request.Headers.TryAddWithoutValidation("x-upsert", "true");
        request.Content = new StreamContent(File.OpenRead(fullPath));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Private artifact upload failed (HTTP {(int)response.StatusCode}). Check the server storage configuration.");
    }

    public async Task<Stream?> OpenAsync(string relative, CancellationToken ct = default)
    {
        if (!IsCloud) return null;
        using var request = Request(HttpMethod.Get, relative);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Private artifact download failed (HTTP {(int)response.StatusCode}).");
        // Keep HTTP credentials on the server. Stream via a delete-on-close temporary file, not a public URL.
        var temporary = Path.Combine(Path.GetTempPath(), "asx-artifact-" + Guid.NewGuid().ToString("N"));
        var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await response.Content.CopyToAsync(file, ct);
            file.Position = 0;
            return file;
        }
        catch { await file.DisposeAsync(); throw; }
    }

    private HttpRequestMessage Request(HttpMethod method, string relative)
    {
        if (!Uri.TryCreate(_options.StorageProjectUrl, UriKind.Absolute, out var project)
            || project.Scheme != "https" || string.IsNullOrWhiteSpace(_options.StorageServiceKey)
            || string.IsNullOrWhiteSpace(_options.StorageBucket))
            throw new InvalidOperationException("Supabase storage requires an HTTPS project URL, private bucket and server-only storage key.");
        var segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidOperationException("Artifact object path is invalid.");
        var uri = project.ToString().TrimEnd('/') + "/storage/v1/object/" + Uri.EscapeDataString(_options.StorageBucket)
            + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.StorageServiceKey);
        request.Headers.Add("apikey", _options.StorageServiceKey);
        return request;
    }
}
