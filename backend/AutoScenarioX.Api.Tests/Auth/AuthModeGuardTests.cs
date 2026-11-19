using AutoScenarioX.Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace AutoScenarioX.Api.Tests.Auth;

/// <summary>Auth:Mode=Dev đăng nhập sẵn mọi request nên không được chạy ngoài Development.</summary>
public class AuthModeGuardTests
{
    private static void Register(string? mode, string environmentName)
    {
        var settings = new Dictionary<string, string?> { ["Auth:ProjectUrl"] = "https://example.supabase.co" };
        if (mode is not null) settings["Auth:Mode"] = mode;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        new ServiceCollection().AddApiAuthentication(configuration, environment);
    }

    [Theory]
    [InlineData("Dev", "Production")]
    [InlineData("Dev", "Staging")]
    [InlineData(null, "Production")]   // không cấu hình gì cũng là Dev
    public void Dev_mode_outside_development_fails_at_startup(string? mode, string environmentName)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(mode, environmentName));
        Assert.Contains("Auth__Mode=Supabase", error.Message);
    }

    [Theory]
    [InlineData("Dev", "Development")]
    [InlineData("Supabase", "Production")]
    public void Allowed_combinations_register(string mode, string environmentName)
        => Register(mode, environmentName);
}
