using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Repositories.Criteria;
using AutoScenarioX.Api.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Tests.Repositories;

/// <summary>Lọc weather/eventType dịch đúng sang SQL (chỉ sinh câu lệnh, không mở kết nối DB).</summary>
public class ScenarioSearchFilterTests
{
    private static string Sql(string? weather = null, string? eventType = null)
    {
        var options = new DbContextOptionsBuilder<AsxDbContext>()
            .UseNpgsql("Host=localhost;Database=none")
            .UseSnakeCaseNamingConvention()
            .Options;
        using var db = new AsxDbContext(options);
        var criteria = new ScenarioSearchCriteria(null, null, null, null, weather, eventType, null, [], 1, 10);
        return ScenarioRepository.ApplyFilters(db.ScenarioRepositoryView, criteria).ToQueryString();
    }

    [Fact]
    public void Weather_filters_on_latest_weather_preset()
    {
        var sql = Sql(weather: "HardRainNoon");
        Assert.Contains("weather_preset =", sql);
        Assert.Contains("HardRainNoon", sql);
    }

    [Fact]
    public void Event_type_uses_jsonb_containment()
    {
        var sql = Sql(eventType: "vehicle_cut_in");
        Assert.Contains("event_types @>", sql);
        Assert.Contains("[\"vehicle_cut_in\"]", sql);
    }

    [Fact]
    public void Without_filters_neither_column_is_used()
    {
        var sql = Sql();
        Assert.DoesNotContain("weather_preset =", sql);
        Assert.DoesNotContain("@>", sql);
    }
}
