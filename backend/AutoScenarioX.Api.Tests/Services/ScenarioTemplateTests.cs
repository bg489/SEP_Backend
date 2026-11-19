using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.DTOs.Scenarios.Requests;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Services.Hashing;
using AutoScenarioX.Api.Services.Implementations;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Services;

/// <summary>Tạo scenario từ mẫu: draft phải sạch cả 3 tầng và giữ nguyên cấu hình (cùng hash) của file mẫu.</summary>
public class ScenarioTemplateTests
{
    public static TheoryData<string> Keys => new(ScenarioContentTemplate.Templates.Select(t => t.Key));

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Template_draft_is_valid_and_keeps_the_configuration(string key)
    {
        var original = ScenarioContentTemplate.LoadTemplate(key)!;
        var draft = JsonNode.Parse(ScenarioContentTemplate.FromTemplate(original, "My scenario", "other"))!.AsObject();

        var result = await TestData.ValidateAsync(draft);
        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
        Assert.Equal("""{"name":"My scenario","category":"other"}""", draft["metadata"]!.ToJsonString());
        Assert.Equal("manual", draft["source"]!["type"]!.GetValue<string>());
        var hasher = new CanonicalJsonHasher();
        Assert.Equal(hasher.ComputeHash(JsonNode.Parse(original)!.AsObject()), hasher.ComputeHash(draft));
    }

    [Fact]
    public async Task Create_uses_the_template_and_rejects_unknown_keys()
    {
        var uow = new FakeUnitOfWork();
        var service = new ScenarioService(uow, FakeUnitOfWork.User(), Options.Create(new ScenarioOptions()));
        ScenarioDraft? saved = null;
        uow.ScenarioDrafts.Add(Arg.Do<ScenarioDraft>(d => saved = d));

        await service.CreateAsync(new CreateScenarioRequest { Name = "Brake test", Category = "braking", Template = "lead_vehicle_brake" });
        Assert.Contains("lead_vehicle_brake", saved!.Content);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new CreateScenarioRequest { Name = "Brake test", Category = "braking", Template = "nope" }));
        Assert.True(ex.Errors.ContainsKey("template"));
        Assert.Equal(1, uow.SaveCount);
    }
}
