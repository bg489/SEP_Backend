namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>Ba tầng validate. Tầng sau chỉ chạy khi tầng trước sạch lỗi.</summary>
public enum ValidationTier
{
    /// <summary>Cấu trúc và miền giá trị - JSON Schema draft 2020-12.</summary>
    Schema = 1,

    /// <summary>Luật nghiệp vụ - tham chiếu, miền tham số, quan hệ giữa các khối.</summary>
    Business = 2,

    /// <summary>Đối chiếu dữ liệu map CARLA thật (bảng asx.map_metadata).</summary>
    Carla = 3
}

public static class ValidationTierExtensions
{
    /// <summary>Chuỗi lưu vào asx.validation_reports.stopped_at (khớp CHECK trong DB).</summary>
    public static string ToDbValue(this ValidationTier tier) => tier switch
    {
        ValidationTier.Schema => "schema",
        ValidationTier.Business => "business",
        ValidationTier.Carla => "carla",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };
}
