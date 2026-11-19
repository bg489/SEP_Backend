using System.Net;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Common.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Auth;

/// <summary>Token giả với "kid" ngẫu nhiên không được kéo JWKS từ Supabase mỗi request.</summary>
public class SupabaseSigningKeysTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"keys\":[]}") });
        }
    }

    [Fact]
    public void Unknown_kids_do_not_reload_jwks_on_every_request()
    {
        var handler = new CountingHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var keys = new SupabaseSigningKeys(factory,
            Options.Create(new AuthOptions { ProjectUrl = "https://example.supabase.co", LegacyJwtSecret = "legacy-secret" }),
            NullLogger<SupabaseSigningKeys>.Instance);

        for (var i = 0; i < 50; i++)
            keys.Resolve("token", null!, $"forged-{i}", null!).ToList();

        // Lần đầu tải 1 lần; kid lạ ngay sau đó dùng lại khoá vừa tải thay vì gọi lại Supabase.
        Assert.Equal(1, handler.Calls);
    }
}
