namespace AutoScenarioX.Api.Common.Options;

/// <summary>Section "Cors": danh sách origin frontend được phép gọi API.</summary>
public sealed class FrontendCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Frontend";
    public string[] AllowedOrigins { get; set; } = [];
}
