# AutoScenarioX: Scenario Schema v1.0

Tài liệu "hợp đồng dữ liệu" của Flow 1 (Scenario Management). Mọi flow khác (Campaign/CARLA, Oracle, Failure Analytics, Prioritization) đọc scenario theo schema này.

## 1. Cấu trúc thư mục

```text
AutoScenarioX/
├── backend/AutoScenarioX.Api/Resources/scenario.schema.json   JSON Schema (draft 2020-12), nguồn sự thật duy nhất
├── backend/AutoScenarioX.Api/Validators/ScenarioContent/      Validator 3 tầng bản C# (bản chạy thật trong API)
├── tools/validate_scenario.py         Validator 3 tầng bản Python (THAM CHIẾU, để đối chiếu với C#)
├── tools/compare_validators.py        Chạy cùng bộ file qua cả hai bản và so kết quả
├── tools/generate_parity_cases.py     Sinh ca nhắm + ca ngẫu nhiên (seed cố định) để đối chiếu mọi nhánh tầng 3
├── tools/export_map_metadata.py       Xuất map metadata từ CARLA cho tầng 3 (chạy 1 lần/map, CẦN máy có CARLA)
├── tools/build_map_metadata_migration.py  Kiểm file metadata rồi sinh SQL nạp asx.map_metadata
├── map_metadata/                      12 map CARLA 0.9.16 đã xuất (Town01–05, Town10HD + bản _Opt)
└── examples/
    ├── S23_pedestrian_crossing_rain.json   hợp lệ, tạo tay, dùng tham số
    ├── S31_cut_in_imported.json            hợp lệ, import từ YAML, 2 event nối tiếp
    ├── INVALID_tier1_schema.json           lỗi cấu trúc / miền giá trị (8 lỗi tầng 1)
    ├── INVALID_tier2_business.json         đúng cấu trúc nhưng sai logic (8 lỗi tầng 2, phủ cả 7 luật)
    ├── INVALID_tier2_param_target_range.json  tham số vượt miền của trường được gắn $param (4 lỗi tầng 2)
    ├── INVALID_tier3_map_not_loaded.json   Town07 không có trong metadata (1 lỗi tầng 3)
    ├── INVALID_tier3_spawn_index.json      Town02 chỉ có index 0..100, dùng 101 (1 lỗi tầng 3)
    ├── INVALID_tier3_npc_count.json        Town02 còn 100 chỗ trống, đòi 101 NPC (1 lỗi tầng 3)
    ├── INVALID_tier3_positions.json        goal trong goal_tolerance, actor trùng spawn point của ego, actor ngoài map (3 lỗi tầng 3)
    ├── INVALID_tier3_route_timeout.json    280 m ở 20 km/h cần 50.3 s nhưng timeout 45 s (1 lỗi tầng 3)
    ├── WARN_tier3_geometry.json            hợp lệ, 3 cảnh báo: timeout sát, trigger kích hoạt ngay, actor xa đường
    └── S23_town05_opt_boundary.json        hợp lệ, đúng biên: Town05_Opt index 301 + 300 NPC
```

Chạy thử:

```bash
python3 -m venv .venv && .venv/bin/pip install jsonschema pyyaml
.venv/bin/python tools/validate_scenario.py examples/*.json
.venv/bin/python tools/validate_scenario.py examples/S23_pedestrian_crossing_rain.json --metadata-dir map_metadata/
```

Đối chiếu hai bản validator (cần API đang chạy; đặt `ASX_TOKEN` nếu `Auth:Mode = Supabase`):

```bash
ASX_METADATA_DIR=map_metadata .venv/bin/python tools/compare_validators.py examples/*.json examples/*.yaml
```

Khi DB đã nạp map metadata thì PHẢI đặt `ASX_METADATA_DIR`, nếu không bản Python chỉ cảnh báo
ở tầng 3 còn bản C# kiểm tra thật, hai bên sẽ lệch.
`ASX_API_BASE` mặc định `https://localhost:7080`.

So trên `(valid, stopped_at, tập (tier, path))` và so nguyên văn thông điệp ở tầng 2/3.
Thông điệp tầng 1 KHÔNG so: bản Python giữ thông điệp của thư viện jsonschema, còn bản C# (từ 2026-10-14)
viết lại thông điệp của JsonSchema.Net thành câu dễ hiểu (`Validators/ScenarioContent/SchemaErrorDescriber.cs`:
"Choose an actor.", "Must be at most 6.", "Goal position is required.", "Enter a number or choose a parameter."...;
giới hạn đọc thẳng từ schema). Path tầng 1 vẫn phải khớp: khoá thừa và tên tham số sai báo tại object cha như Python.

**Kết quả đối chiếu 2026-10-14: 107 file khớp, 0 khác biệt** (89 file dưới đây + 18 ca tầng 1 `s_*` của
`generate_parity_cases.py`).

**Kết quả đối chiếu 2026-10-09 (sau khi thêm 4 luật tầng 3): 89 file khớp, 0 khác biệt**, gồm 13 ví dụ
trong `examples/` và 76 ca do `tools/generate_parity_cases.py` sinh (16 ca nhắm từng nhánh + 60 ca ngẫu nhiên
trên 12 map, seed 20261009), so cả nguyên văn thông điệp tầng 3:

```bash
.venv/bin/python tools/generate_parity_cases.py /tmp/asx-parity
ASX_METADATA_DIR=map_metadata .venv/bin/python tools/compare_validators.py examples/*.json examples/*.yaml /tmp/asx-parity/*.json
```

**Kết quả đối chiếu 2026-10-08 (DB đã nạp 12 map): 10/10 file khớp, 0 khác biệt**, gồm 4 ca tầng 3.

**Kết quả đối chiếu 2026-09-22: 4/4 file khớp, 0 khác biệt.** `content_hash` cũng khớp giữa
`CanonicalJsonHasher` (C#) và `compute_content_hash()` (Python).

## 2. Nguyên tắc thiết kế

**Tài liệu JSON = nội dung của một ScenarioVersion.** Các trường vòng đời do database quản lý và không nằm trong JSON: `scenario_id`, `version_id`, `version_number`, `status` (DRAFT/READY/DEPRECATED/ARCHIVED), `content_hash`, `created_by`, `created_at`.

**Version bất biến.** Sửa scenario thì tạo version mới. Campaign (Flow 2) luôn tham chiếu `version_id`, không tham chiếu `scenario_id`.

**Chuẩn hoá trước khi lưu.** Trước khi tính hash và lưu, Scenario Service phải: (1) điền các giá trị `default` còn thiếu, (2) serialize với key đã sắp xếp. Nhờ vậy hai tài liệu tương đương luôn có cùng hash.

**Quy tắc `content_hash`.** Hash = SHA-256 của các khối ảnh hưởng tới thực thi: `map, route, weather, ego_vehicle, traffic, actors, events, parameters, execution`. Không tính `metadata` và `source`. Hệ quả: đổi mô tả hoặc tag không sinh version mới; nếu người dùng "lưu version mới" mà hash không đổi, hệ thống báo "không có thay đổi ảnh hưởng tới thực thi". Tag nên lưu ở mức Scenario trong DB, `metadata.tags` chỉ dùng khi import/export.

**Tách scenario khỏi SUT.** Scenario chỉ mô tả *môi trường và tình huống*. Việc chọn agent (Behavior Agent cautious/normal/aggressive, Autopilot...) thuộc cấu hình Campaign (Flow 2), để cùng một scenario có thể kiểm thử nhiều cấu hình SUT.

**Strict.** Hầu hết object đặt `additionalProperties: false` để bắt lỗi gõ sai tên trường. Dữ liệu mở rộng đặt trong `extensions`.

## 3. Các khối chính

| Khối | Bắt buộc | Nội dung | Flow nào dùng |
|---|---|---|---|
| `schema_version` | ✔ | luôn là `"1.0"` | tất cả (migrate) |
| `metadata` | ✔ | name, description, category, tags | UI, Flow 5 (feature `category`) |
| `source` | ✔ | manual / import / clone / variant, file gốc, hash gốc, parent version | traceability, Flow 4 |
| `map` | ✔ | tên map CARLA | Flow 2 |
| `route` | ✔ | start, waypoints, goal, speed limit, goal tolerance | Flow 2, Flow 3 (speeding, route completion) |
| `weather` | ✔ | preset + ghi đè tham số `WeatherParameters` | Flow 2, Flow 5 |
| `ego_vehicle` | ✔ | blueprint, role_name, tốc độ ban đầu, `target_speed_kmh` (tốc độ thử, runner giới hạn SUT ở mức này; hai trường tốc độ nhận `$param` từ 2026-10-20) | Flow 2 |
| `traffic` | | NPC và pedestrian nền (Traffic Manager) | Flow 2, Flow 5 |
| `actors` | | actor đặt cố định, được event điều khiển | Flow 2 |
| `events` | | tình huống kịch bản + trigger | Flow 2, Flow 4, Flow 5 |
| `parameters` | | biến có miền giá trị, tham chiếu bằng `{"$param": "ten"}` | Flow 4 (sinh biến thể), Flow 5 |
| `execution` | ✔ | seed, timeout, fixed_delta_seconds, synchronous_mode, warmup | Flow 2 (reproducibility) |

### Vị trí (`location`)
Một trong hai dạng: `{"spawn_point_index": 12}` (khuyến nghị, ổn định và dễ validate) hoặc tọa độ thế giới `{"x":..,"y":..,"z":..,"yaw":..}` (dùng cho pedestrian hay vật cản cần đặt chính xác).

### Event types v1.0

| type | actor_ref phải là | params bắt buộc |
|---|---|---|
| `pedestrian_crossing` | pedestrian | `speed_ms` (+ `direction`, `sync` {point, offset_s}: chờ rồi đi để tới point cùng lúc ego, từ 2026-10-21) |
| `vehicle_cut_in` | vehicle | `side`, `cut_in_gap_m` (+ `lane_change_duration_s`) |
| `lead_vehicle_brake` | vehicle | `deceleration_ms2` (+ `target_speed_kmh`, `time_gap_s` 0.3–5: giữ actor cách ego N giây trước khi phanh, từ 2026-10-21) |
| `traffic_light_override` | *(không có)* | `state`, `duration_s` |
| `vehicle_crossing` (2026-10-19) | vehicle | `destination` (location), `speed_kmh` (+ `ignore_traffic_lights`, mặc định false; + `sync` {`point`, `offset_s`}) |

`vehicle_crossing`: actor đứng ở đường cắt ngang; khi trigger kích hoạt thì chạy qua nút giao tới `destination` ở
`speed_kmh` (CARLA tự tìm đường, nên đi thẳng/rẽ tuỳ vị trí đích). `ignore_traffic_lights = true` cho xe vượt đèn đỏ.
Phủ NHTSA #30/#31/#27/#29/#16 (xem `docs/scenario-typology.md`).
`sync` (tuỳ chọn) đồng bộ thời điểm như OpenSCENARIO SynchronizeAction: actor chỉnh tốc độ (tối đa `speed_kmh`) để tới
`sync.point` cùng lúc ego, lệch `offset_s` giây (−5..5, âm = tới trước ego). Nhờ vậy xung đột xảy ra dù ego nhanh/chậm,
và `offset_s` thành tham số độ khó trực tiếp cho biến thể (mẫu S52–S55 dùng `arrival_offset_s`). Tầng 3 kiểm `destination` như các vị trí khác
(spawn index hợp lệ, toạ độ trong vùng map, cảnh báo nếu xa spawn point).

Trigger (5 loại; 2 loại cuối thêm 2026-10-17, tương thích ngược nên vẫn `schema_version: "1.0"`):

| type | trường | kích hoạt khi | nên dùng cho |
|---|---|---|---|
| `ego_distance_to_actor` | `actor_ref`, `distance_m` | ego cách actor ≤ distance_m | người đi bộ đứng chờ, vật cản |
| `ego_distance_to_location` | `location`, `distance_m` | ego cách vị trí ≤ distance_m | đèn đỏ ở vạch dừng, điểm mốc trên route |
| `simulation_time` | `time_s` | tới giây thứ time_s | chỉ khi không phụ thuộc vị trí ego (giờ khó đoán vì traffic) |
| `after_event` | `event_ref`, `delay_s` | delay_s giây sau khi event `event_ref` bắt đầu | nối tiếp event (chen làn → phanh) |
| `time_to_collision` | `actor_ref`, `ttc_s` | khoảng cách ego–actor / tốc độ tiếp cận ≤ ttc_s (chỉ khi đang tiến lại gần) | căn độ nguy hiểm kiểu Euro NCAP |

Tham khảo: OpenSCENARIO `ReachPositionCondition`/`RelativeDistanceCondition`/`TimeToCollisionCondition`/
`StoryboardElementStateCondition`, CARLA ScenarioRunner `InTriggerDistanceTo*`. `SynchronizeAction` (actor tự điều tốc
gặp ego đúng điểm) chưa có – hướng mở rộng.

### Tham số hoá
```json
"trigger": { "type": "ego_distance_to_actor", "actor_ref": "ped_01",
             "distance_m": { "$param": "trigger_distance_m" } },
"parameters": {
  "trigger_distance_m": { "type": "number", "value": 10, "min": 5, "max": 30, "step": 5, "unit": "m" }
}
```
Sinh biến thể (Flow 4 "Request re-run", hoặc bộ scenario thực nghiệm) = lấy version READY làm gốc
và đổi `parameters.*.value`, ví dụ 5 / 10 / 20 m. B12 tạo một scenario riêng + draft đã validate
cho mỗi tổ hợp, `source.type = "variant"` và `parent_version_id` trỏ tới version gốc.
Sau khi publish, mỗi biến thể có version READY riêng. Xem [API và bộ thực nghiệm B12](variant-generation.md).

## 4. Validation 3 tầng

| Tầng | Công cụ | Ví dụ lỗi bắt được |
|---|---|---|
| 1. Schema | JSON Schema | thiếu trường, sai kiểu, `town05` sai định dạng, speed 300, preset không tồn tại, import thiếu `original_hash` |
| 2. Business | Python | tham số ngoài [min,max], `$param` chưa khai báo, tham số gắn bằng `$param` vượt miền schema của trường đó (value, min/max, integer), actor_ref không tồn tại / sai loại, trigger sau timeout (kể cả chuỗi `after_event` có gốc theo giờ), `after_event` chờ event không tồn tại hoặc chờ vòng lại chính nó, actor bị event điều khiển mà để autopilot, start = goal |
| 3. CARLA-aware | map metadata JSON | map không có, spawn_point_index vượt số spawn point, NPC nhiều hơn spawn point còn trống, toạ độ nằm ngoài map, actor chồng lên ego/actor khác, goal nằm trong goal_tolerance quanh start, không thể tới đích trước timeout nếu không chạy quá tốc độ; cảnh báo toạ độ xa mọi spawn point, timeout sát, trigger khoảng cách đã thoả ngay tại điểm xuất phát |

Tầng sau chỉ chạy khi tầng trước sạch lỗi. Kết quả trả về dạng report `{valid, stopped_at, errors[{tier, path, message}], warnings}`; `path` giúp UI đưa người dùng về đúng bước wizard bị lỗi.

### Tầng 3 chi tiết (từ 2026-10-09, validator `csharp-1.1`)

Mọi luật dưới đây chỉ chạy khi map có trong `asx.map_metadata`; vị trí khai báo bằng spawn point được tra ra
toạ độ thật, vị trí bằng toạ độ lấy x/y/z (z mặc định 0.5). `$param` được giải bằng giá trị hiện tại.

| Luật | Mức | Điều kiện | Path |
|---|---|---|---|
| MapExistsRule | lỗi | map không có trong metadata (chưa nạp metadata nào thì chỉ cảnh báo) | `/map/name` |
| SpawnPointIndexRule | lỗi | `spawn_point_index` ≥ số spawn point | `.../spawn_point_index` |
| NpcCountRule | lỗi | số NPC > spawn point trống (trừ ego và actor đặt bằng spawn point) | `/traffic/npc_vehicles/count` |
| MapPositionRule | lỗi | toạ độ nằm ngoài hình chữ nhật bao các spawn point quá **50 m** | vị trí, vd `/actors/0/spawn` |
| MapPositionRule | cảnh báo | trong vùng đó nhưng cách mọi spawn point **> 50 m** (2D) | vị trí |
| SpawnOverlapRule | lỗi | actor dùng cùng spawn point, hoặc cách **< 1 m** (3D), với ego (route.start) hay actor khai trước | `/actors/i` |
| RouteFeasibilityRule | lỗi | không có waypoint và goal cách start (3D) ≤ `goal_tolerance_m` | `/route/goal_tolerance_m` |
| RouteFeasibilityRule | lỗi | đường thẳng qua các điểm route (2D) ÷ `speed_limit_kmh` > `timeout_s`; `ego_vehicle.target_speed_kmh` thấp hơn limit (hoặc không có limit) thì chia cho target speed (2026-10-20) | `/execution/timeout_s` |
| RouteFeasibilityRule | cảnh báo | thời gian tối thiểu đó > **50%** `timeout_s` | `/execution/timeout_s` |
| TriggerAtStartRule | cảnh báo | trigger `ego_distance_to_actor`/`_to_location` đã thoả ở điểm xuất phát (3D) | `/events/i/trigger/distance_m` |
| TriggerAtStartRule | cảnh báo | trigger `time_to_collision`: khoảng cách start–actor (3D) ÷ `initial_speed_kmh` của ego ≤ `ttc_s` (2026-10-20) | `/events/i/trigger/ttc_s` |

Lý do thiết kế:
- **Chỉ có spawn point, không có hình học đường.** Spawn point phủ hầu hết làn lái (cặp làn cách ~4 m, khoảng hở dọc
  đường thường < 30 m) nhưng không phủ mọi con đường, nên "xa spawn point" chỉ là cảnh báo; "ngoài vùng map" đủ chắc để
  là lỗi. Lề 50 m rộng hơn vỉa hè và khoảng hở lớn nhất đo được (42 m ở Town05).
- **Speed limit là ngưỡng Oracle bắt lỗi chạy quá tốc độ.** Đường thẳng là cận dưới của quãng đường thật, nên nếu chạy
  thẳng ở đúng giới hạn vẫn quá timeout thì SUT tuân luật chắc chắn timeout, còn run về đích chắc chắn đã vi phạm:
  scenario không thể có kết quả đạt. Không khai `speed_limit_kmh` (Oracle dùng biển báo của map) thì bỏ qua.
- **2D hay 3D.** So với mạng đường và đo quãng đường tối thiểu dùng khoảng cách ngang (toạ độ có thể mang z mặc định,
  đường thẳng 2D vẫn là cận dưới); khoảng cách giữa hai vị trí của scenario dùng 3D như `carla.Location.distance`
  (Town03/Town04 có cầu vượt cao tới 8–12 m).
- **Route có waypoint** được phép kết thúc gần điểm xuất phát (vòng lặp), nên luật goal_tolerance chỉ áp khi không có waypoint.

**Hướng mở rộng tầng 3 (deep validate với CARLA thật):** kiểm tra điểm đặt nằm trên làn lái được
(`Map.get_waypoint(..., project_to_road=True)`) và route từ start tới goal đi được (`GlobalRoutePlanner`). Muốn làm
offline thì `tools/export_map_metadata.py` cần xuất thêm topology (vd `Map.get_topology()` hoặc `generate_waypoints`).

## 5. Việc cần chốt với các thành viên khác

Hợp đồng bàn giao chi tiết (API, execution package, lưu ý runner): [handoff-flow2.md](handoff-flow2.md).

| Với | Nội dung |
|---|---|
| SV2 | Runner đọc trực tiếp JSON này (khuyến nghị) hay qua ScenarioRunner/OpenSCENARIO? Xác nhận cách hiện thực 4 event type và 5 trigger. Xác nhận cấu hình SUT nằm ở Campaign. |
| SV3 | Oracle lấy `route.speed_limit_kmh` và `route.goal_tolerance_m` từ scenario; ngưỡng TTC và freezing lấy từ cấu hình Admin. |
| SV4 | Dùng `POST /scenarios/{id}/versions/{n}/variant-batches` với `parameterGrid`, sau đó publish draft của biến thể cần chạy lại. |
| SV5 | Danh sách feature tĩnh lấy từ scenario: category, map, weather, số NPC/pedestrian, số và loại event, giá trị tham số. |

## 6. Cách mở rộng schema

Thêm event type mới: (1) thêm vào `enum` của `event.type`, (2) thêm `$defs/params<TenMoi>`, (3) thêm nhánh `if/then` trong `event.allOf`, (4) cập nhật `EVENT_ACTOR_TYPE` trong validator, (5) SV2 hiện thực trong runner.

Thay đổi không tương thích ngược thì tăng `schema_version` (ví dụ `"2.0"`) và viết hàm migrate `1.0 → 2.0`.
