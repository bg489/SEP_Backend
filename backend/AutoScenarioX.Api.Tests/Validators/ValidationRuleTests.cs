using System.Text.Json.Nodes;
using AutoScenarioX.Api.Tests.Support;
using AutoScenarioX.Api.Validators.ScenarioContent;

namespace AutoScenarioX.Api.Tests.Validators;

/// <summary>Từng luật và cơ chế dừng theo tầng, dựng từ S23 hợp lệ rồi sửa đúng một chỗ.</summary>
public class ValidationRuleTests
{
    private static JsonObject S23() => TestData.LoadExample("S23_pedestrian_crossing_rain.json");

    private static async Task<ScenarioValidationResult> Validate(Action<JsonObject> change, bool metadataLoaded = true)
    {
        var content = S23();
        change(content);
        return await TestData.ValidateAsync(content, metadataLoaded);
    }

    /// <summary>
    /// Đổi map của S23 và dời ped_01 tới cạnh spawn point #0 của map đó: toạ độ gốc của S23 thuộc Town05,
    /// để nguyên thì luật MapPositionRule báo actor nằm ngoài map mới.
    /// </summary>
    private static void UseMap(JsonObject content, string map)
    {
        content["map"]!["name"] = map;
        var anchor = ScenarioGeometry.SpawnPoints(TestData.Map(map))[0];
        content["actors"]![0]!["spawn"] = new JsonObject { ["x"] = anchor.X + 3, ["y"] = anchor.Y, ["z"] = anchor.Z + 0.5 };
    }

    private static void AssertSingleWarning(ScenarioValidationResult result, string path)
    {
        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ValidationTier.Carla, warning.Tier);
        Assert.Equal(path, warning.Path);
    }

    private static void AssertSingleError(ScenarioValidationResult result, ValidationTier tier, string path)
    {
        Assert.False(result.IsValid);
        Assert.Equal(tier, result.StoppedAt);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.Path);
    }

    [Fact]
    public async Task Schema_error_stops_before_business_rules()
    {
        // Sai cả tầng 1 (speed 300) lẫn tầng 2 (start = goal): chỉ báo tầng 1.
        var result = await Validate(c =>
        {
            c["route"]!["speed_limit_kmh"] = 300;
            c["route"]!["goal"] = c["route"]!["start"]!.DeepClone();
        });
        AssertSingleError(result, ValidationTier.Schema, "/route/speed_limit_kmh");
    }

    [Fact]
    public async Task Start_equal_goal_is_business_error()
        => AssertSingleError(await Validate(c => c["route"]!["goal"] = c["route"]!["start"]!.DeepClone()),
            ValidationTier.Business, "/route/goal");

    [Fact]
    public async Task Undeclared_param_reference_is_business_error()
    {
        var result = await Validate(c => c["events"]![0]!["params"]!["speed_ms"] = new JsonObject { ["$param"] = "missing" });
        Assert.Equal(ValidationTier.Business, result.StoppedAt);
        Assert.Contains(result.Errors, e => e.Path == "/events/0/params/speed_ms" && e.Message.Contains("'missing'"));
    }

    [Fact]
    public async Task Parameter_value_outside_its_range_is_business_error()
        => AssertSingleError(await Validate(c => c["parameters"]!["trigger_distance_m"]!["value"] = 31),
            ValidationTier.Business, "/parameters/trigger_distance_m/value");

    [Fact]
    public async Task Parameter_range_wider_than_target_field_is_business_error()
    {
        // speed_ms của pedestrian_crossing chỉ nhận 0.3..6 (ParamTargetRangeRule).
        var result = await Validate(c => c["parameters"]!["pedestrian_speed_ms"]!["max"] = 20);
        AssertSingleError(result, ValidationTier.Business, "/events/0/params/speed_ms");
    }

    [Fact]
    public async Task Trigger_actor_must_exist()
        => AssertSingleError(await Validate(c => c["events"]![0]!["trigger"]!["actor_ref"] = "ghost"),
            ValidationTier.Business, "/events/0/trigger/actor_ref");

    [Fact]
    public async Task Event_driven_actor_cannot_use_autopilot()
        => AssertSingleError(await Validate(c => c["actors"]![0]!["behavior"] = "autopilot"),
            ValidationTier.Business, "/actors/0/behavior");

    [Fact]
    public async Task Trigger_after_timeout_is_business_error()
    {
        var result = await Validate(c => c["events"]![0]!["trigger"] = new JsonObject
        {
            ["type"] = "simulation_time", ["time_s"] = 500
        });
        AssertSingleError(result, ValidationTier.Business, "/events/0/trigger/time_s");
    }

    [Fact]
    public async Task Without_map_metadata_tier3_only_warns()
    {
        var result = await Validate(c => c["map"]!["name"] = "Town07", metadataLoaded: false);
        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ValidationTier.Carla, warning.Tier);
        Assert.Equal("/map/name", warning.Path);
    }

    [Theory]
    [InlineData("Town02", 100, true)]    // Town02 có 101 điểm: index cuối là 100
    [InlineData("Town02", 101, false)]
    [InlineData("Town10HD", 154, true)]
    [InlineData("Town10HD", 155, false)]
    public async Task Spawn_index_must_exist_on_map(string map, int index, bool valid)
    {
        var result = await Validate(c =>
        {
            UseMap(c, map);
            c["route"]!["start"]!["spawn_point_index"] = index;
            c["route"]!["goal"]!["spawn_point_index"] = 0;
        });
        Assert.Equal(valid, result.IsValid);
        if (!valid) AssertSingleError(result, ValidationTier.Carla, "/route/start/spawn_point_index");
    }

    [Theory]
    [InlineData(100, true)]    // 101 điểm - 1 ego = 100 chỗ trống
    [InlineData(101, false)]
    public async Task Npc_count_limited_by_free_spawn_points(int npc, bool valid)
    {
        var result = await Validate(c =>
        {
            UseMap(c, "Town02");
            c["traffic"]!["npc_vehicles"]!["count"] = npc;
        });
        Assert.Equal(valid, result.IsValid);
        if (!valid) AssertSingleError(result, ValidationTier.Carla, "/traffic/npc_vehicles/count");
    }

    [Fact]
    public async Task Npc_count_given_by_param_uses_parameter_value()
    {
        var result = await Validate(c =>
        {
            UseMap(c, "Town02");
            c["parameters"]!["npc_count"]!["max"] = 300;
            c["parameters"]!["npc_count"]!["value"] = 150;
        });
        AssertSingleError(result, ValidationTier.Carla, "/traffic/npc_vehicles/count");
    }

    // ---- Tầng 3 mở rộng (2026-10-09): vị trí, chồng spawn, route, trigger tại điểm xuất phát ----
    // S23 trên Town05: start #12 (-44.3, -58.58), goal #87 cách 38 m, ped_01 (-30, -80.9) trên vỉa hè đường hướng đông, cách spawn point gần nhất 8.5 m.

    private static JsonObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y, ["z"] = 1.0 };

    [Fact]
    public async Task Coordinates_outside_the_map_are_a_carla_error()
    {
        var result = await Validate(c => c["actors"]![0]!["spawn"] = Point(400, 0));
        AssertSingleError(result, ValidationTier.Carla, "/actors/0/spawn");
        Assert.Equal("Actor 'ped_01' is 189 m outside map 'Town05', beyond the area covered by its spawn points.",
            result.Errors[0].Message);
    }

    [Theory]
    [InlineData(49, true)]    // trong lề 50 m, và spawn point gần nhất cách 49 m nên không cảnh báo
    [InlineData(51, false)]
    public async Task Off_map_margin_is_50_metres(double beyond, bool valid)
    {
        var edge = ScenarioGeometry.SpawnPoints(TestData.Map("Town05")).Values.MaxBy(p => p.X)!;
        var result = await Validate(c => c["actors"]![0]!["spawn"] = Point(edge.X + beyond, edge.Y));
        Assert.Equal(valid, result.IsValid);
        if (valid) Assert.Empty(result.Warnings);
        else AssertSingleError(result, ValidationTier.Carla, "/actors/0/spawn");
    }

    [Fact]
    public async Task Coordinates_far_from_every_spawn_point_only_warn()
    {
        var result = await Validate(c => c["actors"]![0]!["spawn"] = Point(105, 58));   // giữa khu nhà, cách 70 m
        AssertSingleWarning(result, "/actors/0/spawn");
    }

    [Fact]
    public async Task Trigger_location_is_checked_against_the_map()
    {
        var result = await Validate(c => c["events"]![0]!["trigger"] = new JsonObject
        {
            ["type"] = "ego_distance_to_location", ["location"] = Point(-400, 0), ["distance_m"] = 10
        });
        AssertSingleError(result, ValidationTier.Carla, "/events/0/trigger/location");
    }

    [Fact]
    public async Task Actor_on_the_ego_spawn_point_is_a_carla_error()
    {
        var result = await Validate(c => c["actors"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "veh_01", ["type"] = "vehicle", ["blueprint"] = "vehicle.audi.a2",
            ["spawn"] = new JsonObject { ["spawn_point_index"] = 12 }, ["behavior"] = "idle"
        }));
        AssertSingleError(result, ValidationTier.Carla, "/actors/1");
        Assert.Contains("on top of the ego vehicle: both use spawn point #12", result.Errors[0].Message);
    }

    [Theory]
    [InlineData(0.4, false)]
    [InlineData(1.5, true)]
    public async Task Actors_closer_than_one_metre_overlap(double gap, bool valid)
    {
        var result = await Validate(c => c["actors"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "ped_02", ["type"] = "pedestrian", ["blueprint"] = "walker.pedestrian.0002",
            ["spawn"] = Point(-30 + gap, -80.9), ["behavior"] = "idle"
        }));
        Assert.Equal(valid, result.IsValid);
        if (!valid) AssertSingleError(result, ValidationTier.Carla, "/actors/1");
    }

    [Theory]
    [InlineData(5, false)]   // spawn point #11 cách start #12 khoảng 3.7 m
    [InlineData(3, true)]
    public async Task Goal_inside_the_goal_tolerance_is_a_carla_error(double tolerance, bool valid)
    {
        var result = await Validate(c =>
        {
            c["route"]!["goal"] = new JsonObject { ["spawn_point_index"] = 11 };
            c["route"]!["goal_tolerance_m"] = tolerance;
        });
        Assert.Equal(valid, result.IsValid);
        if (valid) return;
        AssertSingleError(result, ValidationTier.Carla, "/route/goal_tolerance_m");
        Assert.Equal("The goal is 3.7 m from the start, within the 5 m goal tolerance, so route completion would be met as soon as the run starts.",
            result.Errors[0].Message);
    }

    [Fact]
    public async Task Loop_route_through_a_waypoint_may_end_near_the_start()
    {
        var result = await Validate(c =>
        {
            c["route"]!["goal"] = new JsonObject { ["spawn_point_index"] = 11 };
            c["route"]!["waypoints"] = new JsonArray(new JsonObject { ["spawn_point_index"] = 87 });
        });
        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(20, 45, "error")]     // 279.5 m ở 20 km/h cần 50.3 s > 45 s
    [InlineData(40, 40, "warning")]   // cần 25.2 s, quá nửa timeout 40 s
    [InlineData(40, 60, "none")]
    public async Task Straight_line_at_the_speed_limit_must_fit_the_timeout(double speed, double timeout, string outcome)
    {
        var result = await Validate(c =>
        {
            c["route"]!["goal"] = new JsonObject { ["spawn_point_index"] = 234 };
            c["route"]!["speed_limit_kmh"] = speed;
            c["execution"]!["timeout_s"] = timeout;
        });
        switch (outcome)
        {
            case "error":
                AssertSingleError(result, ValidationTier.Carla, "/execution/timeout_s");
                Assert.Equal("Even in a straight line at the 20 km/h speed limit, the ego needs at least 50.3 s to cover 279.5 m between the route points, but the run times out after 45 s. Only a run that breaks the speed limit could finish.",
                    result.Errors[0].Message);
                break;
            case "warning":
                AssertSingleWarning(result, "/execution/timeout_s");
                break;
            default:
                Assert.True(result.IsValid);
                Assert.Empty(result.Warnings);
                break;
        }
    }

    [Fact]
    public async Task Without_a_speed_limit_the_timeout_is_not_checked()
    {
        var result = await Validate(c =>
        {
            c["route"]!["goal"] = new JsonObject { ["spawn_point_index"] = 234 };
            c["route"]!.AsObject().Remove("speed_limit_kmh");
            c["execution"]!["timeout_s"] = 10;
        });
        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(10, true)]    // ped_01 cách điểm xuất phát 7.7 m, trigger 10 m (qua $param) kích hoạt ngay
    [InlineData(5, false)]
    public async Task Distance_trigger_already_met_at_the_start_warns(double triggerDistance, bool warns)
    {
        var result = await Validate(c =>
        {
            c["actors"]![0]!["spawn"] = Point(-40, -65);
            c["parameters"]!["trigger_distance_m"]!["value"] = triggerDistance;
        });
        Assert.True(result.IsValid);
        if (warns) AssertSingleWarning(result, "/events/0/trigger/distance_m");
        else Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Location_trigger_already_met_at_the_start_warns()
    {
        var result = await Validate(c => c["events"]![0]!["trigger"] = new JsonObject
        {
            ["type"] = "ego_distance_to_location",
            ["location"] = new JsonObject { ["spawn_point_index"] = 11 },
            ["distance_m"] = 5
        });
        AssertSingleWarning(result, "/events/0/trigger/distance_m");
    }

    [Theory]
    [InlineData(36, true)]    // ped_01 cách điểm xuất phát 7.7 m, ở 36 km/h còn 0.8 s ≤ ttc 2 s
    [InlineData(0, false)]    // xuất phát đứng yên thì chưa có tốc độ tiếp cận
    public async Task Time_to_collision_trigger_already_met_at_the_start_warns(double initialSpeed, bool warns)
    {
        var result = await Validate(c =>
        {
            c["actors"]![0]!["spawn"] = Point(-40, -65);
            c["ego_vehicle"]!["initial_speed_kmh"] = initialSpeed;
            c["events"]![0]!["trigger"] = new JsonObject
            {
                ["type"] = "time_to_collision", ["actor_ref"] = "ped_01", ["ttc_s"] = 2
            };
        });
        Assert.True(result.IsValid);
        if (!warns)
        {
            Assert.Empty(result.Warnings);
            return;
        }
        AssertSingleWarning(result, "/events/0/trigger/ttc_s");
        Assert.Equal("The ego starts 7.7 m from actor 'ped_01' at 36 km/h, 0.8 s away, within the 2 s trigger time, so event 'ev_ped_cross' fires as soon as the run starts.",
            result.Warnings[0].Message);
    }

    [Theory]
    [InlineData(20, "error")]     // target 20 < limit 40: 279.5 m cần 50.3 s > 45 s
    [InlineData(60, "warning")]   // target 60 > limit 40 thì tính theo limit: 25.2 s, quá nửa timeout
    public async Task Ego_target_speed_below_the_limit_sets_the_route_time(double target, string outcome)
    {
        var result = await Validate(c =>
        {
            c["route"]!["goal"] = new JsonObject { ["spawn_point_index"] = 234 };
            c["route"]!["speed_limit_kmh"] = 40;
            c["execution"]!["timeout_s"] = 45;
            c["ego_vehicle"]!["target_speed_kmh"] = new JsonObject { ["$param"] = "ego_speed_kmh" };
            c["parameters"]!["ego_speed_kmh"] = new JsonObject
            {
                ["type"] = "number", ["value"] = target, ["min"] = 10, ["max"] = 60, ["step"] = 5
            };
        });
        if (outcome == "warning")
        {
            AssertSingleWarning(result, "/execution/timeout_s");
            Assert.StartsWith("In a straight line at the 40 km/h speed limit,", result.Warnings[0].Message);
            return;
        }
        AssertSingleError(result, ValidationTier.Carla, "/execution/timeout_s");
        Assert.Equal("Even in a straight line at the 20 km/h ego target speed, the ego needs at least 50.3 s to cover 279.5 m between the route points, but the run times out after 45 s. Only a run faster than the target speed could finish.",
            result.Errors[0].Message);
    }

    // ---- Trigger mở rộng (2026-10-17): after_event, time_to_collision ----

    /// <summary>Thêm event đổi đèn (không cần actor) với trigger cho trước.</summary>
    private static void AddLightEvent(JsonObject c, string id, JsonObject trigger)
        => c["events"]!.AsArray().Add(new JsonObject
        {
            ["id"] = id, ["type"] = "traffic_light_override", ["trigger"] = trigger,
            ["params"] = new JsonObject { ["state"] = "red", ["duration_s"] = 10 }
        });

    private static JsonObject After(string eventRef, double delay)
        => new() { ["type"] = "after_event", ["event_ref"] = eventRef, ["delay_s"] = delay };

    [Fact]
    public async Task After_event_chain_is_valid()
        => Assert.True((await Validate(c => AddLightEvent(c, "ev_light", After("ev_ped_cross", 3)))).IsValid);

    [Fact]
    public async Task After_event_must_reference_an_existing_event()
    {
        var result = await Validate(c => AddLightEvent(c, "ev_light", After("ev_missing", 3)));
        AssertSingleError(result, ValidationTier.Business, "/events/1/trigger/event_ref");
        Assert.Equal("The trigger waits for event 'ev_missing', which is not defined in events.", result.Errors[0].Message);
    }

    [Fact]
    public async Task Event_waiting_for_itself_never_starts()
    {
        var result = await Validate(c => AddLightEvent(c, "ev_light", After("ev_light", 3)));
        AssertSingleError(result, ValidationTier.Business, "/events/1/trigger/event_ref");
        Assert.Equal("Event 'ev_light' waits for itself through after_event triggers, so it never starts.", result.Errors[0].Message);
    }

    [Fact]
    public async Task Events_waiting_for_each_other_are_both_reported()
    {
        var result = await Validate(c =>
        {
            AddLightEvent(c, "ev_a", After("ev_b", 1));
            AddLightEvent(c, "ev_b", After("ev_a", 1));
        });
        Assert.Equal(["/events/1/trigger/event_ref", "/events/2/trigger/event_ref"], result.Errors.Select(e => e.Path));
    }

    [Fact]
    public async Task Chained_trigger_after_the_timeout_never_runs()
    {
        var result = await Validate(c =>
        {
            AddLightEvent(c, "ev_first", new JsonObject { ["type"] = "simulation_time", ["time_s"] = 100 });
            AddLightEvent(c, "ev_second", After("ev_first", 30));
        });
        AssertSingleError(result, ValidationTier.Business, "/events/2/trigger/delay_s");
        Assert.Equal("The trigger fires at 130s, counting the events it waits for, but the simulation ends at 120s. The event will never run.",
            result.Errors[0].Message);
    }

    [Fact]
    public async Task Chain_rooted_in_a_distance_trigger_has_no_known_fire_time()
        => Assert.True((await Validate(c => AddLightEvent(c, "ev_light", After("ev_ped_cross", 500)))).IsValid);

    [Fact]
    public async Task Time_to_collision_trigger_needs_a_known_actor()
    {
        var result = await Validate(c => c["events"]![0]!["trigger"] = new JsonObject
        {
            ["type"] = "time_to_collision", ["actor_ref"] = "ped_99", ["ttc_s"] = 2
        });
        AssertSingleError(result, ValidationTier.Business, "/events/0/trigger/actor_ref");
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(20, false)]
    public async Task Time_to_collision_is_limited_to_ten_seconds(double ttc, bool valid)
    {
        var result = await Validate(c => c["events"]![0]!["trigger"] = new JsonObject
        {
            ["type"] = "time_to_collision", ["actor_ref"] = "ped_01", ["ttc_s"] = ttc
        });
        Assert.Equal(valid, result.IsValid);
        if (!valid) Assert.Equal(ValidationTier.Schema, result.StoppedAt);
    }

    // ---- vehicle_crossing (2026-10-19) ----

    /// <summary>Thêm xe veh_cross ở spawn #188 (Town05) và event vehicle_crossing chạy tới destination.</summary>
    private static void AddCrossing(JsonObject c, JsonObject destination, string actorRef = "veh_cross")
    {
        c["actors"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "veh_cross", ["type"] = "vehicle", ["blueprint"] = "vehicle.audi.a2",
            ["spawn"] = new JsonObject { ["spawn_point_index"] = 188 }, ["behavior"] = "scripted"
        });
        c["events"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "ev_cross", ["type"] = "vehicle_crossing", ["actor_ref"] = actorRef,
            ["trigger"] = new JsonObject { ["type"] = "simulation_time", ["time_s"] = 5 },
            ["params"] = new JsonObject { ["destination"] = destination, ["speed_kmh"] = 30 }
        });
    }

    // ---- pedestrian_crossing.sync (2026-10-21, Euro NCAP CPNA) ----

    [Theory]
    [InlineData(-44.3, -73.6, true)]     // giữa làn ego trước ped_01
    [InlineData(-400, 0, false)]         // ngoài vùng map: MapPositionRule bắt như mọi vị trí
    public async Task Pedestrian_sync_point_is_checked_like_any_position(double x, double y, bool valid)
    {
        var result = await Validate(c => c["events"]![0]!["params"]!["sync"] = new JsonObject
        {
            ["point"] = Point(x, y), ["offset_s"] = 0.25
        });
        Assert.Equal(valid, result.IsValid);
        if (!valid) AssertSingleError(result, ValidationTier.Carla, "/events/0/params/sync/point");
    }

    // ---- lead_vehicle_brake.time_gap_s (2026-10-21, Euro NCAP CCRb) ----

    /// <summary>Thêm xe veh_lead ở spawn #188 và event lead_vehicle_brake với time_gap_s cho trước.</summary>
    private static void AddLeadBrake(JsonObject c, JsonNode timeGap)
    {
        c["actors"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "veh_lead", ["type"] = "vehicle", ["blueprint"] = "vehicle.audi.a2",
            ["spawn"] = new JsonObject { ["spawn_point_index"] = 188 }, ["behavior"] = "scripted"
        });
        c["events"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "ev_brake", ["type"] = "lead_vehicle_brake", ["actor_ref"] = "veh_lead",
            ["trigger"] = new JsonObject { ["type"] = "simulation_time", ["time_s"] = 5 },
            ["params"] = new JsonObject { ["deceleration_ms2"] = 4, ["time_gap_s"] = timeGap }
        });
    }

    [Theory]
    [InlineData(1.0, null)]
    [InlineData(10.0, ValidationTier.Schema)]    // ngoài 0.3..5
    public async Task Lead_brake_time_gap_is_limited(double gap, ValidationTier? stoppedAt)
    {
        var result = await Validate(c => AddLeadBrake(c, JsonValue.Create(gap)));
        Assert.Equal(stoppedAt is null, result.IsValid);
        if (stoppedAt is not null) AssertSingleError(result, stoppedAt.Value, "/events/1/params/time_gap_s");
    }

    [Fact]
    public async Task Lead_brake_time_gap_parameter_must_fit_the_field_range()
    {
        // Tham số tự cho 0..10 nhưng time_gap_s chỉ nhận 0.3..5: ParamTargetRangeRule bắt ở tầng 2
        var result = await Validate(c =>
        {
            c["parameters"]!["gap_s"] = new JsonObject { ["type"] = "number", ["value"] = 1, ["min"] = 0, ["max"] = 10 };
            AddLeadBrake(c, new JsonObject { ["$param"] = "gap_s" });
        });
        Assert.False(result.IsValid);
        Assert.Equal(ValidationTier.Business, result.StoppedAt);
        Assert.All(result.Errors, e => Assert.Equal("/events/1/params/time_gap_s", e.Path));
    }

    [Fact]
    public async Task Crossing_vehicle_to_a_spawn_point_is_valid()
        => Assert.True((await Validate(c => AddCrossing(c, new JsonObject { ["spawn_point_index"] = 128 }))).IsValid);

    [Fact]
    public async Task Crossing_event_needs_a_vehicle()
    {
        var result = await Validate(c => AddCrossing(c, new JsonObject { ["spawn_point_index"] = 128 }, "ped_01"));
        AssertSingleError(result, ValidationTier.Business, "/events/1/actor_ref");
    }

    [Fact]
    public async Task Crossing_destination_must_be_on_the_map()
    {
        var result = await Validate(c => AddCrossing(c, new JsonObject { ["x"] = -400, ["y"] = 0 }));
        AssertSingleError(result, ValidationTier.Carla, "/events/1/params/destination");
        Assert.Equal("The destination of event 'ev_cross' is 124.4 m outside map 'Town05', beyond the area covered by its spawn points.",
            result.Errors[0].Message);
    }

    private static void AddSync(JsonObject c, JsonObject point, JsonNode? offset = null)
    {
        var sync = new JsonObject { ["point"] = point };
        if (offset is not null) sync["offset_s"] = offset;
        c["events"]![1]!["params"]!["sync"] = sync;
    }

    [Fact]
    public async Task Synchronised_crossing_is_valid()
        => Assert.True((await Validate(c =>
        {
            AddCrossing(c, new JsonObject { ["spawn_point_index"] = 128 });
            AddSync(c, new JsonObject { ["x"] = -195.1, ["y"] = 6.6 }, 0.5);
        })).IsValid);

    [Fact]
    public async Task Sync_point_must_be_on_the_map()
    {
        var result = await Validate(c =>
        {
            AddCrossing(c, new JsonObject { ["spawn_point_index"] = 128 });
            AddSync(c, new JsonObject { ["x"] = -400, ["y"] = 0 });
        });
        AssertSingleError(result, ValidationTier.Carla, "/events/1/params/sync/point");
    }

    [Fact]
    public async Task Sync_offset_parameter_must_stay_within_five_seconds()
    {
        var result = await Validate(c =>
        {
            AddCrossing(c, new JsonObject { ["spawn_point_index"] = 128 });
            AddSync(c, new JsonObject { ["spawn_point_index"] = 128 }, new JsonObject { ["$param"] = "trigger_distance_m" });
        });
        AssertSingleError(result, ValidationTier.Business, "/events/1/params/sync/offset_s");
    }
}
