using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Common.Json;

/// <summary>
/// Điền các giá trị "default" khai báo trong Resources/scenario.schema.json.
/// Chạy TRƯỚC khi tính content_hash (để hai tài liệu tương đương có cùng hash)
/// và trước validation tầng 2/3 (để luật không phải xử lý trường thiếu).
///
/// Hàm này CỐ TÌNH khoan dung: gặp kiểu dữ liệu sai thì bỏ qua, không ném lỗi,
/// vì hash phải tính được cả khi nội dung chưa hợp lệ (validation_reports.content_hash NOT NULL).
/// Sai kiểu là việc của validation tầng 1.
/// </summary>
public static class ScenarioContentNormalizer
{
    public static JsonObject Normalize(JsonElement content)
    {
        var root = JsonNode.Parse(content.GetRawText()) as JsonObject ?? new JsonObject();
        return Normalize(root);
    }

    public static JsonObject Normalize(JsonObject root)
    {
        SetDefault(root, "actors", new JsonArray());
        SetDefault(root, "events", new JsonArray());
        SetDefault(root, "parameters", new JsonObject());

        if (root["route"] is JsonObject route) NormalizeRoute(route);
        if (root["weather"] is JsonObject weather) SetDefault(weather, "preset", "ClearNoon");
        if (root["ego_vehicle"] is JsonObject ego) NormalizeEgoVehicle(ego);
        if (root["traffic"] is JsonObject traffic) NormalizeTraffic(traffic);
        if (root["execution"] is JsonObject execution) NormalizeExecution(execution);
        if (root["actors"] is JsonArray actors) NormalizeActors(actors);
        if (root["events"] is JsonArray events) NormalizeEvents(events);

        return root;
    }

    private static void NormalizeRoute(JsonObject route)
    {
        SetDefault(route, "goal_tolerance_m", 5);
        NormalizeLocation(route["start"]);
        NormalizeLocation(route["goal"]);
        if (route["waypoints"] is JsonArray waypoints)
            foreach (var waypoint in waypoints) NormalizeLocation(waypoint);
    }

    private static void NormalizeEgoVehicle(JsonObject ego)
    {
        SetDefault(ego, "blueprint", "vehicle.tesla.model3");
        SetDefault(ego, "role_name", "hero");
        SetDefault(ego, "initial_speed_kmh", 0);
    }

    private static void NormalizeTraffic(JsonObject traffic)
    {
        if (traffic["npc_vehicles"] is JsonObject npc)
        {
            SetDefault(npc, "count", 0);
            SetDefault(npc, "blueprint_filter", "vehicle.*");
            SetDefault(npc, "four_wheels_only", true);
            SetDefault(npc, "speed_difference_percent", 0);
            SetDefault(npc, "distance_to_leading_m", 2.5);
        }

        if (traffic["pedestrians"] is JsonObject pedestrians)
        {
            SetDefault(pedestrians, "count", 0);
            SetDefault(pedestrians, "crossing_probability", 0);
            SetDefault(pedestrians, "running_probability", 0);
        }
    }

    private static void NormalizeExecution(JsonObject execution)
    {
        SetDefault(execution, "fixed_delta_seconds", 0.05);
        SetDefault(execution, "synchronous_mode", true);
        SetDefault(execution, "warmup_s", 2);
    }

    private static void NormalizeActors(JsonArray actors)
    {
        foreach (var node in actors)
        {
            if (node is not JsonObject actor) continue;
            SetDefault(actor, "initial_speed_kmh", 0);
            SetDefault(actor, "behavior", "idle");
            NormalizeLocation(actor["spawn"]);
        }
    }

    private static void NormalizeEvents(JsonArray events)
    {
        foreach (var node in events)
        {
            if (node is not JsonObject evt) continue;

            if (evt["trigger"] is JsonObject trigger) NormalizeLocation(trigger["location"]);
            if (evt["params"] is not JsonObject parameters) continue;

            switch (evt["type"]?.GetValue<string>())
            {
                case "pedestrian_crossing":
                    SetDefault(parameters, "direction", "left_to_right");
                    NormalizeSync(parameters);
                    break;
                case "vehicle_cut_in":
                    SetDefault(parameters, "lane_change_duration_s", 2);
                    break;
                case "lead_vehicle_brake":
                    SetDefault(parameters, "target_speed_kmh", 0);
                    break;
                case "traffic_light_override":
                    SetDefault(parameters, "target", "next_on_route");
                    break;
                case "vehicle_crossing":
                    SetDefault(parameters, "ignore_traffic_lights", false);
                    NormalizeLocation(parameters["destination"]);
                    NormalizeSync(parameters);
                    break;
            }
        }
    }

    private static void NormalizeSync(JsonObject parameters)
    {
        if (parameters["sync"] is not JsonObject sync) return;
        SetDefault(sync, "offset_s", 0);
        NormalizeLocation(sync["point"]);
    }

    /// <summary>Dạng toạ độ thế giới ({x, y}) có default z = 0.5; dạng spawn_point_index không có default.</summary>
    private static void NormalizeLocation(JsonNode? node)
    {
        if (node is JsonObject location && location.ContainsKey("x")) SetDefault(location, "z", 0.5);
    }

    private static void SetDefault(JsonObject target, string key, JsonNode value)
    {
        if (!target.ContainsKey(key)) target[key] = value;
    }
}
