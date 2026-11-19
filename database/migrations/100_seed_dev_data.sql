-- =====================================================================
-- AutoScenarioX - Dữ liệu mẫu cho DEV (KHÔNG dùng cho môi trường thật)
-- Chạy trong Supabase SQL Editor sau migration 001. Chạy lại nhiều lần không lỗi.
--  * 1 user dev (chưa liên kết Supabase Auth)
--  * SCN-A: Pedestrian Crossing - đã publish v1 (READY)
--  * SCN-B: Cut-in - mới có bản nháp (DRAFT), chưa publish
--  content_hash đã kiểm chứng 2026-09-22 bằng CẢ HAI bản validator (CanonicalJsonHasher bên C#
--  và tools/validate_scenario.py bên Python) - hai bên ra cùng giá trị. Sửa nội dung scenario
--  thì phải tính lại hash, nếu không uq_versions_hash sẽ mất tác dụng với hàng seed.
-- Muốn xoá: chạy 000_reset_asx_schema.sql rồi chạy lại 001.
-- =====================================================================
INSERT INTO asx.app_users (id, email, display_name, role)
VALUES ('d0000000-0000-0000-0000-000000000001', 'dev.engineer@autoscenariox.local', 'Dev Scenario Engineer', 'SCENARIO_ENGINEER')
ON CONFLICT (id) DO NOTHING;

INSERT INTO asx.scenarios (id, name, description, category, origin_type, created_by)
VALUES ('d1000000-0000-0000-0000-000000000001', 'Pedestrian Crossing - Heavy Rain', 'Scenario mẫu: người đi bộ băng qua đường khi mưa to', 'pedestrian', 'manual', 'd0000000-0000-0000-0000-000000000001'),
       ('d1000000-0000-0000-0000-000000000002', 'Highway Cut-in Then Hard Brake', 'Scenario mẫu: xe chen làn rồi phanh gấp (đang soạn)', 'cut_in', 'manual', 'd0000000-0000-0000-0000-000000000001')
ON CONFLICT (id) DO NOTHING;

INSERT INTO asx.validation_reports (id, scenario_id, content_hash, is_valid, schema_version, validator_version, created_by)
VALUES ('d2000000-0000-0000-0000-000000000001', 'd1000000-0000-0000-0000-000000000001', 'sha256:9cdc2cdf6e31eb2f7b433c0e5fb0db94f86c3e2b9cf92338c914f586a96c7881', true, '1.0', 'seed', 'd0000000-0000-0000-0000-000000000001')
ON CONFLICT (id) DO NOTHING;

INSERT INTO asx.scenario_versions (id, scenario_id, version_number, schema_version, content, content_hash,
                                   source_type, validation_report_id, change_note, published_by)
VALUES ('d3000000-0000-0000-0000-000000000001', 'd1000000-0000-0000-0000-000000000001', 1, '1.0',
'{
 "schema_version": "1.0",
 "metadata": {
  "name": "Pedestrian Crossing - Heavy Rain",
  "description": "Người đi bộ bất ngờ băng qua đường khi ego còn cách một khoảng trigger_distance_m, trời mưa to. Tọa độ pedestrian chỉ mang tính minh hoạ, cần lấy lại từ map metadata thật.",
  "category": "pedestrian",
  "tags": [
   "pedestrian",
   "urban",
   "rain",
   "vru"
  ]
 },
 "source": {
  "type": "manual"
 },
 "map": {
  "name": "Town05"
 },
 "route": {
  "start": {
   "spawn_point_index": 12
  },
  "goal": {
   "spawn_point_index": 87
  },
  "speed_limit_kmh": 40,
  "goal_tolerance_m": 5
 },
 "weather": {
  "preset": "HardRainNoon",
  "fog_density": 10
 },
 "ego_vehicle": {
  "blueprint": "vehicle.tesla.model3",
  "role_name": "hero"
 },
 "traffic": {
  "npc_vehicles": {
   "count": {
    "$param": "npc_count"
   }
  },
  "pedestrians": {
   "count": 5,
   "crossing_probability": 0.1
  }
 },
 "actors": [
  {
   "id": "ped_01",
   "type": "pedestrian",
   "blueprint": "walker.pedestrian.0001",
   "spawn": {
    "x": 20.5,
    "y": -60.2,
    "z": 1.0,
    "yaw": 90
   },
   "behavior": "scripted"
  }
 ],
 "events": [
  {
   "id": "ev_ped_cross",
   "type": "pedestrian_crossing",
   "description": "ped_01 băng qua đường khi ego tới gần",
   "trigger": {
    "type": "ego_distance_to_actor",
    "actor_ref": "ped_01",
    "distance_m": {
     "$param": "trigger_distance_m"
    }
   },
   "actor_ref": "ped_01",
   "params": {
    "speed_ms": {
     "$param": "pedestrian_speed_ms"
    },
    "direction": "left_to_right"
   }
  }
 ],
 "parameters": {
  "trigger_distance_m": {
   "type": "number",
   "value": 10,
   "min": 5,
   "max": 30,
   "step": 5,
   "unit": "m",
   "description": "Khoảng cách ego-pedestrian lúc pedestrian bắt đầu băng qua"
  },
  "pedestrian_speed_ms": {
   "type": "number",
   "value": 1.5,
   "min": 0.8,
   "max": 3.0,
   "step": 0.2,
   "unit": "m/s"
  },
  "npc_count": {
   "type": "integer",
   "value": 10,
   "min": 0,
   "max": 40,
   "step": 10
  }
 },
 "execution": {
  "seed": 42,
  "timeout_s": 120,
  "fixed_delta_seconds": 0.05,
  "synchronous_mode": true,
  "warmup_s": 2
 }
}'::jsonb,
'sha256:9cdc2cdf6e31eb2f7b433c0e5fb0db94f86c3e2b9cf92338c914f586a96c7881', 'manual', 'd2000000-0000-0000-0000-000000000001', 'Phiên bản đầu tiên (seed)', 'd0000000-0000-0000-0000-000000000001')
ON CONFLICT (id) DO NOTHING;

UPDATE asx.scenarios SET latest_version_id = 'd3000000-0000-0000-0000-000000000001', latest_version_number = 1
WHERE id = 'd1000000-0000-0000-0000-000000000001' AND latest_version_id IS NULL;

INSERT INTO asx.scenario_drafts (scenario_id, content, updated_by)
VALUES ('d1000000-0000-0000-0000-000000000002',
'{
 "schema_version": "1.0",
 "metadata": {
  "name": "Highway Cut-in Then Hard Brake",
  "description": "Xe NPC chen vào làn ego rồi phanh gấp. Scenario được import từ file YAML của nhóm.",
  "category": "cut_in",
  "tags": [
   "highway",
   "cut-in",
   "braking"
  ]
 },
 "source": {
  "type": "import",
  "format": "yaml",
  "original_file": "cut_in_highway.yaml",
  "original_hash": "sha256:3b9f1c0e5a7d2f4b8c6e1a9d0f3b5c7e9a1d3f5b7c9e1a3d5f7b9c1e3a5d7f9b",
  "import_warnings": [
   "Trường ''npc.color'' không được hỗ trợ, đã bỏ qua"
  ]
 },
 "map": {
  "name": "Town04"
 },
 "route": {
  "start": {
   "spawn_point_index": 3
  },
  "waypoints": [
   {
    "spawn_point_index": 150
   }
  ],
  "goal": {
   "spawn_point_index": 210
  },
  "speed_limit_kmh": 90
 },
 "weather": {
  "preset": "ClearSunset"
 },
 "ego_vehicle": {
  "blueprint": "vehicle.lincoln.mkz_2020"
 },
 "traffic": {
  "npc_vehicles": {
   "count": 20,
   "speed_difference_percent": -10
  }
 },
 "actors": [
  {
   "id": "npc_cutter",
   "type": "vehicle",
   "blueprint": "vehicle.audi.a2",
   "spawn": {
    "spawn_point_index": 4
   },
   "initial_speed_kmh": 80,
   "behavior": "scripted"
  }
 ],
 "events": [
  {
   "id": "ev_cut_in",
   "type": "vehicle_cut_in",
   "trigger": {
    "type": "simulation_time",
    "time_s": 15
   },
   "actor_ref": "npc_cutter",
   "params": {
    "side": "left",
    "cut_in_gap_m": {
     "$param": "cut_in_gap_m"
    },
    "lane_change_duration_s": 2
   }
  },
  {
   "id": "ev_brake",
   "type": "lead_vehicle_brake",
   "trigger": {
    "type": "simulation_time",
    "time_s": 20
   },
   "actor_ref": "npc_cutter",
   "params": {
    "deceleration_ms2": {
     "$param": "brake_decel"
    },
    "target_speed_kmh": 0
   }
  }
 ],
 "parameters": {
  "cut_in_gap_m": {
   "type": "number",
   "value": 12,
   "min": 4,
   "max": 30,
   "step": 4,
   "unit": "m"
  },
  "brake_decel": {
   "type": "number",
   "value": 6,
   "min": 3,
   "max": 9,
   "step": 1,
   "unit": "m/s2"
  }
 },
 "execution": {
  "seed": 7,
  "timeout_s": 90
 }
}'::jsonb,
'd0000000-0000-0000-0000-000000000001')
ON CONFLICT (scenario_id) DO NOTHING;

-- Kiểm tra nhanh
SELECT code, name, latest_version_number, map_name, weather_preset, has_draft
FROM asx.v_scenario_repository ORDER BY code;
