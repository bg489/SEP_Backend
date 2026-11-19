using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Common.Constants;

/// <summary>
/// Sinh nội dung bản nháp mẫu khi tạo scenario mới.
/// Kết quả PHẢI hợp lệ ở validation tầng 1 (Resources/scenario.schema.json)
/// để người dùng mở wizard lên là đã có khung đầy đủ để sửa.
/// </summary>
public static class ScenarioContentTemplate
{
    /// <summary>Map mặc định của bản nháp mẫu (chỉ là gợi ý, người dùng đổi trong wizard).</summary>
    public const string DefaultMap = "Town03";

    /// <summary>
    /// Mẫu kịch bản dựng sẵn cho người mới (nội dung lấy từ examples/, nhúng vào DLL).
    /// Reference = tình huống tương ứng trong NHTSA pre-crash typology (DOT HS 810 767, tỉ lệ ở Table 13) / Euro NCAP;
    /// bảng đối chiếu đầy đủ ở docs/scenario-typology.md.
    /// </summary>
    public sealed record Template(string Key, string Title, string Summary, string Category, string? Reference);

    public static readonly IReadOnlyList<Template> Templates =
    [
        new("pedestrian_crossing", "Pedestrian steps out",
            "A pedestrian on the right kerb crosses the ego's lane just before an intersection.", "pedestrian",
            "NHTSA #12 Pedestrian Crash Without Prior Vehicle Maneuver (0.7% of crashes) · Euro NCAP CPNA"),
        new("lead_vehicle_brake", "Lead vehicle brakes",
            "The car 42 m ahead in the same highway lane brakes hard to a stop when the ego reaches a set point on the road.", "braking",
            "NHTSA #25 Lead Vehicle Decelerating (7.2% of crashes) · Euro NCAP CCRb"),
        new("stopped_vehicle", "Stopped vehicle ahead",
            "A car stands still in the ego's highway lane 100 m ahead; the ego must stop behind it or change lanes.", "obstacle",
            "NHTSA #26 Lead Vehicle Stopped (16.4% of crashes, the most frequent) · Euro NCAP CCRs"),
        new("slow_vehicle", "Slow vehicle ahead",
            "A car drives at 20 km/h in the ego's highway lane 100 m ahead; the ego must slow down behind it or change lanes.", "following",
            "NHTSA #24 Lead Vehicle Moving at Lower Constant Speed · Euro NCAP CCRm"),
        new("cut_in_brake", "Cut-in then hard brake",
            "A car from the left lane cuts in front of the ego, then brakes hard right after, both at set points on the road.", "cut_in",
            "NHTSA #18 Vehicle(s) Changing Lanes – Same Direction (5.7% of crashes), then #25 Lead Vehicle Decelerating"),
        new("red_light", "Red light at intersection",
            "The next traffic light on the route turns red while the ego approaches it.", "traffic_light",
            "NHTSA #4 Running Red Light (4.3% of crashes)"),
        new("crossing_vehicle", "Car crosses the junction",
            "A car waiting on the side road drives straight across the junction, through a red light, timed to reach the crossing point together with the ego.", "intersection",
            "NHTSA #30 Straight Crossing Paths (4.4% of crashes) and #4 Running Red Light · Euro NCAP CCCscp"),
        new("left_turn_oncoming", "Left turn across oncoming car",
            "The ego turns left at a junction while an oncoming car, timed to arrive with it, drives straight through; the ego must wait for the gap.", "intersection",
            "NHTSA #27/#29 Left Turn Across Path From Opposite Directions (6.9% of crashes) · Euro NCAP CCFtap"),
        new("turning_into_path", "Car turns into the ego lane",
            "A car from the side road on the right turns right into the ego's lane, timed to merge one second ahead of it.", "intersection",
            "NHTSA #31 Vehicle(s) Turning at Non-Signalized Junctions (7.3% of crashes, third most frequent)"),
        new("same_direction_turn", "Car in next lane turns across",
            "A car waiting in the lane to the ego's left turns right across the ego's lane at the junction, timed to cut across one second ahead of it.", "intersection",
            "NHTSA #16 Vehicle(s) Turning – Same Direction (3.7% of crashes)"),
        new("open_road", "Open road speed limit",
            "No scripted events: the ego drives a highway route under a strict speed limit with background traffic.", "free_driving",
            null)
    ];

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // giữ nguyên tiếng Việt trong mô tả thay vì \uXXXX
    };

    /// <summary>Nội dung gốc của mẫu; null nếu key không có trong <see cref="Templates"/>.</summary>
    public static string? LoadTemplate(string key)
    {
        if (Templates.All(t => t.Key != key)) return null;
        var resource = $"AutoScenarioX.Api.Templates.{key}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Không tìm thấy embedded resource '{resource}'. Kiểm tra EmbeddedResource trong .csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Bản nháp từ mẫu: giữ cấu hình, thay metadata bằng tên/category của scenario mới và đánh dấu nguồn manual.</summary>
    public static string FromTemplate(string templateJson, string name, string category)
    {
        // JsonNode giữ nguyên text số (1.0 vs 1) nên content_hash không đổi so với file mẫu.
        var root = JsonNode.Parse(templateJson)!.AsObject();
        root["metadata"] = new JsonObject { ["name"] = name, ["category"] = category };
        root["source"] = new JsonObject { ["type"] = "manual" };
        return root.ToJsonString(Indented);
    }

    public static string Create(string name, string category)
    {
        // Serialize để chuỗi do người dùng nhập được escape đúng chuẩn JSON.
        var jsonName = JsonSerializer.Serialize(name);
        var jsonCategory = JsonSerializer.Serialize(category);

        return $$"""
        {
          "schema_version": "1.0",
          "metadata": {
            "name": {{jsonName}},
            "category": {{jsonCategory}}
          },
          "source": {
            "type": "manual"
          },
          "map": {
            "name": "{{DefaultMap}}"
          },
          "route": {
            "start": { "spawn_point_index": 0 },
            "goal": { "spawn_point_index": 1 }
          },
          "weather": {
            "preset": "ClearNoon"
          },
          "ego_vehicle": {
            "blueprint": "vehicle.tesla.model3"
          },
          "traffic": {},
          "actors": [],
          "events": [],
          "parameters": {},
          "execution": {
            "seed": 42,
            "timeout_s": 120
          }
        }
        """;
    }
}
