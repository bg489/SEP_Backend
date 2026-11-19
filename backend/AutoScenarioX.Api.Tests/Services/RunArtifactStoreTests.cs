using System.Net;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Services.Implementations;
using Microsoft.Extensions.Options;

namespace AutoScenarioX.Api.Tests.Services;

public sealed class RunArtifactStoreTests
{
    private static TelemetryOptions Cloud() => new() { StorageProvider = "Supabase", StorageProjectUrl = "https://fixture.example.invalid", StorageServiceKey = "fixture-server-key", StorageBucket = "private evidence" };

    [Fact]
    public async Task Private_upload_sends_server_credentials_and_encoded_object_path()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "real-file-bytes");
            using var http = new HttpClient(new Handler(async request => {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/storage/v1/object/private%20evidence/run%201/video.mp4", request.RequestUri!.AbsolutePath);
                Assert.Equal("fixture-server-key", request.Headers.Authorization!.Parameter);
                Assert.Equal("video/mp4", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal("real-file-bytes", await request.Content.ReadAsStringAsync());
                return new(HttpStatusCode.OK);
            }));
            await new RunArtifactStore(http, Options.Create(Cloud())).UploadAsync("run 1/video.mp4", path, "video/mp4");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Private_download_returns_authenticated_bytes_without_a_public_url()
    {
        using var http = new HttpClient(new Handler(request => {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });
        }));
        await using var stream = await new RunArtifactStore(http, Options.Create(Cloud())).OpenAsync("run/video.mp4");
        Assert.NotNull(stream); using var copy = new MemoryStream(); await stream.CopyToAsync(copy);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, copy.ToArray());
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("run/../../outside")]
    [InlineData("run//video.mp4")]
    public async Task Invalid_object_paths_are_blocked_before_network_access(string path)
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not contact storage")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RunArtifactStore(http, Options.Create(Cloud())).OpenAsync(path));
    }

    [Fact]
    public async Task Storage_failure_is_reported_instead_of_claiming_a_successful_upload()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var error = await Assert.ThrowsAsync<IOException>(() => new RunArtifactStore(http, Options.Create(Cloud())).OpenAsync("run/video.mp4"));
        Assert.Contains("403", error.Message); Assert.DoesNotContain("fixture-server-key", error.Message);
    }

    [Fact]
    public async Task Filesystem_provider_makes_no_cloud_request()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not contact storage")));
        var store = new RunArtifactStore(http, Options.Create(new TelemetryOptions()));
        Assert.False(store.IsCloud); Assert.Null(await store.OpenAsync("run/video.mp4"));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
