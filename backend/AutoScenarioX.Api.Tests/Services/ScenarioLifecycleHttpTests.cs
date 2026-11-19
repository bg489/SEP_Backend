using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoScenarioX.Api.Common.Auth;
using AutoScenarioX.Api.Controllers.Auth;
using AutoScenarioX.Api.DTOs.Campaigns;
using AutoScenarioX.Api.DTOs.FailureAnalysis;
using AutoScenarioX.Api.DTOs.Oracle;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.UnitOfWork;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Services.Interfaces;
using AutoScenarioX.Api.Tests.Support;
using AutoScenarioX.Api.Validators.ScenarioContent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Real HTTP/controllers/services/validator with isolated repository storage; no live DB/CARLA is claimed.</summary>
public class ScenarioLifecycleHttpTests
{
    [Theory]
    [InlineData(AppRoles.TestEngineer)]
    [InlineData(AppRoles.TestReviewer)]
    public async Task Readonly_roles_can_read_published_profile_but_cannot_edit_it(string role)
    {
        using var factory = new LifecycleFactory();
        using var engineer = factory.Client(AppRoles.ScenarioEngineer);
        var created = await engineer.PostAsJsonAsync("/api/v1/scenarios", new { name = "Shared scenario metadata", category = "other" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var reader = factory.Client(role);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/v1/scenarios/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PatchAsJsonAsync($"/api/v1/scenarios/{id}", new { name = "Forbidden edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/v1/scenarios/{id}/draft")).StatusCode);
    }

    [Fact]
    public async Task Create_save_validate_publish_new_draft_archive_restore_keeps_published_version()
    {
        using var factory = new LifecycleFactory();
        using var client = factory.Client(AppRoles.ScenarioEngineer);
        var created = await client.PostAsJsonAsync("/api/v1/scenarios", new { name = "HTTP lifecycle", category = "pedestrian" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var draftUrl = $"/api/v1/scenarios/{id}/draft";
        var versionsUrl = $"/api/v1/scenarios/{id}/versions";

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(versionsUrl, new { })).StatusCode);
        var draft = await client.GetAsync(draftUrl);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var content = TestData.LoadExample("S23_pedestrian_crossing_rain.json");
        content["source"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "manual" };
        content["metadata"]!["name"] = "HTTP lifecycle";
        using var save = new HttpRequestMessage(HttpMethod.Put, draftUrl) { Content = JsonContent.Create(new { content }) };
        save.Headers.IfMatch.Add(draft.Headers.ETag!);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(save)).StatusCode);
        var validation = await client.PostAsync(draftUrl + "/validate", null);
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        var report = await validation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(report.GetProperty("isValid").GetBoolean(), report.ToString());

        var published = await client.PostAsJsonAsync(versionsUrl, new { changeNote = "HTTP integration test" });
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var version = await published.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("READY", version.GetProperty("summary").GetProperty("status").GetString());
        var firstHash = version.GetProperty("summary").GetProperty("contentHash").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(draftUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync(draftUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(draftUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/scenarios/{id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync(draftUrl + "/validate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/scenarios/{id}/restore", null)).StatusCode);
        var oldVersion = await client.GetFromJsonAsync<JsonElement>(versionsUrl + "/1");
        Assert.Equal(firstHash, oldVersion.GetProperty("summary").GetProperty("contentHash").GetString());
        var savedRoute = System.Text.Json.Nodes.JsonNode.Parse(factory.Versions.Values.Single().Content)!["route"]!;
        Assert.Equal(content["route"]!["start"]!["spawn_point_index"]!.GetValue<int>(), savedRoute["start"]!["spawn_point_index"]!.GetValue<int>());
        Assert.Equal(content["route"]!["goal"]!["spawn_point_index"]!.GetValue<int>(), savedRoute["goal"]!["spawn_point_index"]!.GetValue<int>());
    }

    [Fact]
    public async Task Stale_or_missing_IfMatch_is_rejected_and_published_versions_have_no_update_route()
    {
        using var factory = new LifecycleFactory();
        using var client = factory.Client(AppRoles.ScenarioEngineer);
        var created = await client.PostAsJsonAsync("/api/v1/scenarios", new { name = "Revision test", category = "other" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var draftUrl = $"/api/v1/scenarios/{id}/draft";
        var originalRevision = factory.Drafts[id].RowVersion;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(draftUrl, new { content = new { } })).StatusCode);
        using var stale = new HttpRequestMessage(HttpMethod.Put, draftUrl) { Content = JsonContent.Create(new { content = new { } }) };
        stale.Headers.TryAddWithoutValidation("If-Match", "\"99\"");
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(stale)).StatusCode);
        Assert.Equal(originalRevision, factory.Drafts[id].RowVersion);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync($"/api/v1/scenarios/{id}/versions/1", new { content = new { } })).StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.TestEngineer)]
    [InlineData(AppRoles.TestReviewer)]
    public async Task Wrong_role_cannot_create_scenarios(string role)
    {
        using var factory = new LifecycleFactory();
        using var client = factory.Client(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/scenarios", new { name = "Forbidden scenario", category = "other" })).StatusCode);
        Assert.Empty(factory.Scenarios);
    }

    [Fact]
    public async Task No_identity_is_401_and_invalid_scenario_has_validation_response()
    {
        using var factory = new LifecycleFactory();
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/scenarios", new { name = "No identity", category = "other" })).StatusCode);
        using var client = factory.Client(AppRoles.ScenarioEngineer);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/scenarios", new { name = "x", category = "unknown" })).StatusCode);
        Assert.Empty(factory.Scenarios);
    }

    [Theory]
    [InlineData(AppRoles.ScenarioEngineer, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.TestEngineer, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.TestReviewer, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.Admin, HttpStatusCode.OK)]
    public async Task Admin_user_endpoint_checks_actual_http_role(string role, HttpStatusCode expected)
    {
        using var factory = new LifecycleFactory();
        using var client = factory.Client(role);
        Assert.Equal(expected, (await client.GetAsync("/api/v1/admin/users")).StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.ScenarioEngineer, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.TestEngineer, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.TestReviewer, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.Admin, HttpStatusCode.OK, HttpStatusCode.OK)]
    public async Task Reviewer_and_admin_policies_apply_to_real_http_routes(string role, HttpStatusCode reviewStatus, HttpStatusCode settingsStatus)
    {
        using var factory = new LifecycleFactory();
        using var client = factory.Client(role);
        Assert.Equal(settingsStatus, (await client.GetAsync("/api/v1/admin/settings")).StatusCode);
        Assert.Equal(settingsStatus, (await client.GetAsync("/api/v1/admin/oracle-configs")).StatusCode);
        Assert.Equal(reviewStatus, (await client.PostAsJsonAsync($"/api/v1/campaigns/{Guid.NewGuid()}/failure-analysis/review", new { decision = "APPROVE", note = "Reviewed" })).StatusCode);
    }

    [Fact]
    public async Task Admin_and_review_routes_require_identity()
    {
        using var factory = new LifecycleFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/v1/campaigns/{Guid.NewGuid()}/failure-analysis/review", new { decision = "APPROVE" })).StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.ScenarioEngineer, false)]
    [InlineData(AppRoles.TestEngineer, true)]
    [InlineData(AppRoles.TestReviewer, false)]
    [InlineData(AppRoles.Admin, true)]
    public async Task Only_test_engineer_and_admin_can_create_edit_start_or_rerun_campaigns(string role, bool allowed)
    {
        using var factory = new LifecycleFactory();
        using var client = factory.Client(role);
        var id = Guid.NewGuid();

        var create = await client.PostAsJsonAsync("/api/v1/campaigns", new { name = "Policy campaign" });
        var edit = await client.PatchAsJsonAsync($"/api/v1/campaigns/{id}", new { name = "Edited campaign" });
        var start = await client.PostAsync($"/api/v1/campaigns/{id}/start", null);
        var rerun = await client.PostAsync($"/api/v1/campaigns/{id}/rerun", null);

        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, rerun.StatusCode);
        await factory.Campaigns.Received(allowed ? 1 : 0).CreateAsync(Arg.Any<CreateCampaignRequest>(), Arg.Any<CancellationToken>());
        await factory.Campaigns.Received(allowed ? 1 : 0).UpdateAsync(id, Arg.Any<UpdateCampaignRequest>(), Arg.Any<CancellationToken>());
        await factory.Campaigns.Received(allowed ? 1 : 0).StartAsync(id, Arg.Any<CancellationToken>());
        await factory.Campaigns.Received(allowed ? 1 : 0).RerunAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Campaign_mutations_without_identity_return_401_before_invoking_services()
    {
        using var factory = new LifecycleFactory();
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/campaigns", new { name = "Anonymous" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsJsonAsync($"/api/v1/campaigns/{id}", new { name = "Anonymous" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/v1/campaigns/{id}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/v1/campaigns/{id}/rerun", null)).StatusCode);
        Assert.Empty(factory.Campaigns.ReceivedCalls());
    }

    public sealed class LifecycleFactory : WebApplicationFactory<AuthController>
    {
        public Dictionary<Guid, Scenario> Scenarios { get; } = [];
        public Dictionary<Guid, ScenarioDraft> Drafts { get; } = [];
        public Dictionary<Guid, ScenarioVersion> Versions { get; } = [];
        public ICampaignService Campaigns { get; } = Substitute.For<ICampaignService>();
        private readonly Dictionary<Guid, ValidationReport> _reports = [];
        private readonly FakeUnitOfWork _uow = new();

        public LifecycleFactory()
        {
            var campaign = new CampaignResponse(Guid.NewGuid(), "CMP-HTTP", "Policy campaign", null,
                "proposed_failure_history", "Queued", 0, 0, 0, "1m", "HTTP test", DateTimeOffset.UtcNow,
                null, new("behavior_agent", "normal", "0.9.16"), [], [], 0);
            Campaigns.CreateAsync(Arg.Any<CreateCampaignRequest>(), Arg.Any<CancellationToken>()).Returns(campaign);
            Campaigns.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateCampaignRequest>(), Arg.Any<CancellationToken>()).Returns(campaign);
            Campaigns.StartAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(campaign);
            Campaigns.RerunAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(campaign);
            _uow.Scenarios.When(r => r.Add(Arg.Any<Scenario>())).Do(call => { var entity = call.Arg<Scenario>(); entity.Code = "SCN-HTTP"; Scenarios[entity.Id] = entity; });
            _uow.Scenarios.GetReadOnlyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Scenarios.GetValueOrDefault(call.Arg<Guid>())));
            _uow.Scenarios.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Scenarios.GetValueOrDefault((Guid)call.Arg<object[]>()[0])));
            _uow.Scenarios.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Scenarios.ContainsKey(call.Arg<Guid>())));
            _uow.ScenarioDrafts.When(r => r.Add(Arg.Any<ScenarioDraft>())).Do(call => { var entity = call.Arg<ScenarioDraft>(); Drafts[entity.ScenarioId] = entity; });
            _uow.ScenarioDrafts.When(r => r.Remove(Arg.Any<ScenarioDraft>())).Do(call => Drafts.Remove(call.Arg<ScenarioDraft>().ScenarioId));
            _uow.ScenarioDrafts.GetReadOnlyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Drafts.GetValueOrDefault(call.Arg<Guid>())));
            _uow.ScenarioDrafts.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Drafts.GetValueOrDefault(call.Arg<Guid>())));
            _uow.ScenarioVersions.When(r => r.Add(Arg.Any<ScenarioVersion>())).Do(call => { var entity = call.Arg<ScenarioVersion>(); Versions[entity.Id] = entity; });
            _uow.ScenarioVersions.GetReadOnlyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Versions.GetValueOrDefault(call.Arg<Guid>())));
            _uow.ScenarioVersions.GetByNumberAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Versions.Values.FirstOrDefault(v => v.ScenarioId == call.Arg<Guid>() && v.VersionNumber == call.Arg<int>())));
            _uow.ScenarioVersions.FindByContentHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Versions.Values.FirstOrDefault(v => v.ScenarioId == call.Arg<Guid>() && v.ContentHash == call.Arg<string>())));
            _uow.ValidationReports.When(r => r.Add(Arg.Any<ValidationReport>())).Do(call => { var entity = call.Arg<ValidationReport>(); _reports[entity.Id] = entity; });
            _uow.ValidationReports.GetReadOnlyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(_reports.GetValueOrDefault(call.Arg<Guid>())));
            _uow.Tags.ListNamesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
            _uow.MapMetadata.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(TestData.AllMaps.Values.ToList());
            _uow.AppUsers.SearchAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(((IReadOnlyList<AppUser>)Array.Empty<AppUser>(), 0));
        }

        public HttpClient Client(string role)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Supabase", "Host=localhost;Database=isolated_http_test;Username=test;Password=test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Mode"] = "Dev", ["ConnectionStrings:Supabase"] = "Host=localhost;Database=isolated_http_test;Username=test;Password=test"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUnitOfWork>();
                services.AddSingleton<IUnitOfWork>(_uow);
                services.RemoveAll<ICampaignService>();
                services.AddSingleton(Campaigns);
                services.RemoveAll<IScenarioValidator>();
                services.AddSingleton<IScenarioValidator>(TestData.CreateValidator());
                services.AddScoped<IAdminUsersService, AdminUsersService>();
                var adminOracle = Substitute.For<IAdminOracleService>();
                adminOracle.SettingsAsync(Arg.Any<CancellationToken>()).Returns(new SystemSettingsResponse(85, 98, 30, false, null, DateTimeOffset.UtcNow, "Not enabled"));
                adminOracle.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<AdminOracleConfigResponse>());
                services.RemoveAll<IAdminOracleService>();
                services.AddSingleton(adminOracle);
                var analysis = Substitute.For<IFailureAnalysisService>();
                analysis.ReviewAsync(Arg.Any<Guid>(), Arg.Any<ReviewFailureReportRequest>(), Arg.Any<CancellationToken>())
                    .Returns(call => new FailureAnalysisResponse(Guid.NewGuid(), call.Arg<Guid>(), "CMP-HTTP", "Policy test", "Reviewed", 0, 0, 0, 0, 0, "Policy response", null, [], [], null));
                services.RemoveAll<IFailureAnalysisService>();
                services.AddSingleton(analysis);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "HttpTest";
                    options.DefaultChallengeScheme = "HttpTest";
                    options.DefaultForbidScheme = "HttpTest";
                }).AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>("HttpTest", _ => { });
            });
        }
    }

    private sealed class TestIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (!AppRoles.All.Contains(role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(AsxClaimTypes.Subject, "d0000000-0000-0000-0000-000000000001"), new Claim(AsxClaimTypes.AppRole, role) };
            var identity = new ClaimsIdentity(claims, Scheme.Name, AsxClaimTypes.Subject, AsxClaimTypes.AppRole);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
