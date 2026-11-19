# Bàn giao Flow 1 → Flow 2 (B13)

Tài liệu hợp đồng giữa Flow 1 (Scenario Management) và Flow 2 (Test Campaign & CARLA Execution),
kèm phần dành cho SV4 (biến thể / chạy lại) và SV5 (ưu tiên scenario). Cập nhật 2026-10-08.

## 1. Ranh giới

Flow 1 kết thúc khi scenario có **version READY bất biến** với `scenario_version_id`. Flow 2 bắt đầu từ
`scenario_version_id`: chọn version vào campaign, lấy gói chạy, dựng scenario trong CARLA, chạy SUT.

- Campaign **luôn tham chiếu `scenario_version_id`**, không tham chiếu `scenario_id` (scenario còn đổi,
  version thì không).
- Flow 2 **không ghi** vào bảng của Flow 1. Muốn đổi scenario thì Scenario Engineer publish version mới.
- Cấu hình SUT (Behavior Agent cautious/normal/aggressive, Autopilot…) thuộc campaign, không nằm trong scenario.

## 2. Hai cách đọc

| Cách | Dùng khi | Ghi chú |
|---|---|---|
| **API (khuyến nghị)** | runner Python trên máy CARLA, frontend Flow 2 | có gói chạy đã giải `$param` và spawn point |
| View `asx.v_ready_scenario_versions` | module Flow 2 nằm trong cùng backend, cần join SQL | chỉ có nội dung gốc (còn `$param`), phải tự giải |

Mọi endpoint cần đăng nhập; **mọi role** đọc được (không cần `ManageScenarios`). Ở `Auth:Mode=Supabase`
gửi `Authorization: Bearer <access token>`.

## 3. API

### 3.1 `GET /api/v1/scenario-versions/ready`

Danh sách version **được phép đưa vào campaign mới**: version `READY` của scenario `ACTIVE`.
Không kèm nội dung (nhẹ, dùng cho màn hình chọn scenario).

| Query | Ý nghĩa |
|---|---|
| `q` | tìm theo tên hoặc mã (`SCN-00001`) |
| `category`, `map` | lọc theo phân loại / tên map |
| `scenarioId` | mọi version READY của một scenario |
| `latestOnly=true` | chỉ bản READY mới nhất của mỗi scenario (`isLatestReady`) |
| `page`, `pageSize` | phân trang (mặc định 20, tối đa 100) |

Mỗi phần tử: `scenarioVersionId, scenarioId, scenarioCode, scenarioName, category, versionNumber,
isLatestReady, contentHash, mapName, weatherPreset, eventTypes[], eventCount, actorCount,
npcVehicleCount, pedestrianCount, speedLimitKmh, egoBlueprint, publishedAt`.

Một scenario có thể có **nhiều version READY** cùng lúc (để chạy lại bản cũ, so v1/v2, APFD).
Mặc định nên chọn bản `isLatestReady = true`; muốn chạy bản cũ thì chọn đúng `scenarioVersionId` của nó.

### 3.2 `GET /api/v1/scenario-versions/{scenarioVersionId}/execution-package`

Gói chạy cho runner. Trả cho **mọi version**, kể cả `DEPRECATED` hoặc scenario đã `ARCHIVED`,
để campaign cũ chạy lại được; campaign **mới** chỉ được chọn khi `selectableForNewCampaign = true`.
Version không tồn tại → 404 `VERSION_NOT_FOUND`.

```jsonc
{
  "packageFormat": "asx.execution-package/1",
  "scenarioVersionId": "…", "scenarioId": "…", "scenarioCode": "SCN-00001",
  "scenarioName": "…", "category": "pedestrian",
  "versionNumber": 1, "versionStatus": "READY", "scenarioStatus": "ACTIVE",
  "selectableForNewCampaign": true,
  "schemaVersion": "1.0",
  "contentHash": "sha256:…",           // định danh nội dung, lưu cùng kết quả chạy
  "publishedAt": "…",
  "map": { "name": "Town05", "carlaVersion": "0.9.16", "spawnPointCount": 302 },
  "parameterValues": { "npc_count": 10, "pedestrian_speed_ms": 1.5, "trigger_distance_m": 10 },
  "locations": [
    { "path": "/route/start", "spawnPointIndex": 12,
      "transform": { "x": -44.3, "y": -58.58, "z": 0.45, "yaw": -90.337 } },
    { "path": "/route/goal", "spawnPointIndex": 87, "transform": { … } }
  ],
  "runnable": true,
  "issues": [],
  "resolvedContent": { … }             // nội dung version, mọi {"$param"} đã thay bằng giá trị
}
```

- `resolvedContent` giữ đúng cấu trúc schema v1.0 (`docs/scenario-schema.md`), đã điền default lúc publish;
  chỉ khác nội dung gốc ở chỗ `{"$param": "x"}` được thay bằng `parameters.x.value`.
  Khối `parameters` vẫn còn để tra cứu. Nội dung gốc (còn `$param`) lấy ở
  `GET /api/v1/scenarios/{scenarioId}/versions/{versionNumber}`.
- `locations`: mỗi object có `spawn_point_index` trong nội dung (route start/goal/waypoints, actor spawn…),
  `path` là JSON Pointer tới object đó trong `resolvedContent`. Vị trí cho bằng toạ độ (`x, y, z, yaw`)
  thì dùng thẳng, không có trong danh sách.
- `runnable = false` khi không dựng được: thiếu map metadata của map, index vượt số spawn point,
  `$param` chưa khai báo. `issues` ghi lý do (tiếng Anh). Runner **không được chạy** gói `runnable = false`.

## 4. Bảo đảm của Flow 1

1. **Bất biến.** Nội dung và `contentHash` của một `scenarioVersionId` không bao giờ đổi (trigger DB).
   Trạng thái chỉ đổi một chiều `READY → DEPRECATED`.
2. **Đã validate 3 tầng** lúc publish: schema, nghiệp vụ, CARLA (map tồn tại, spawn index hợp lệ,
   đủ chỗ cho NPC) theo map metadata **CARLA 0.9.16**.
3. **`contentHash`** = SHA-256 của JSON chuẩn hoá trên các khối thực thi (map, route, weather,
   ego_vehicle, traffic, actors, events, parameters, execution). Đổi mô tả/tag không đổi hash.
4. **Tái lập được:** `execution.seed`, `fixed_delta_seconds`, `synchronous_mode`, `warmup_s`, `timeout_s`
   nằm trong nội dung.

## 5. Lưu ý cho runner (SV2)

- **Phiên bản CARLA phải khớp `map.carlaVersion`** (hiện 0.9.16). Thứ tự spawn point có thể đổi giữa các
  phiên bản; khác phiên bản thì báo lỗi thay vì chạy.
- **Bản `_Opt` khác bản gốc:** `Town05` và `Town05_Opt` có cùng tập spawn point nhưng **thứ tự khác**
  (cùng index lệch tới hàng trăm mét). Nạp đúng map ghi trong `map.name`, dùng `transform` trong gói
  thay vì tự tra `get_spawn_points()[index]`. Nếu vẫn tự tra, phải kiểm tra toạ độ khớp `transform`.
- **Lưu cùng kết quả mỗi lần chạy:** `scenarioVersionId`, `contentHash`, `map.carlaVersion`,
  cấu hình SUT, seed. Flow 3–5 dựa vào bộ này để truy vết.
- Từ 2026-10-09 tầng 3 kiểm toạ độ `x, y` so với spawn point của map (ngoài vùng map > 50 m là lỗi, cách mọi
  spawn point > 50 m là cảnh báo), actor chồng spawn, goal trong goal_tolerance, timeout so với speed limit
  (`docs/scenario-schema.md` mục 4). Metadata **không có hình học đường**, nên vẫn chưa bảo đảm điểm đặt nằm
  trên làn: runner nên `Map.get_waypoint(..., project_to_road=True)` và ghi cảnh báo nếu lệch xa.

Phác thảo dùng gói (Python, CARLA 0.9.16):

```python
pkg = api.get(f"/api/v1/scenario-versions/{version_id}/execution-package")
assert pkg["runnable"], pkg["issues"]
assert client.get_server_version() == pkg["map"]["carlaVersion"]
c = pkg["resolvedContent"]
world = client.load_world(pkg["map"]["name"])
loc = {l["path"]: l["transform"] for l in pkg["locations"]}
start = loc["/route/start"]   # {"x","y","z","yaw"} -> carla.Transform(Location(x,y,z), Rotation(yaw=yaw))
# weather: c["weather"]; ego: c["ego_vehicle"]; traffic: c["traffic"]; actors/events/trigger: c["actors"], c["events"]
# đồng bộ: c["execution"]["synchronous_mode"], ["fixed_delta_seconds"], seed cho Traffic Manager: ["seed"]
```

Tốc độ của ego (2026-10-20, kiểu Euro NCAP "VUT test speed"):
- `ego_vehicle.initial_speed_kmh`: spawn ego rồi đặt vận tốc ban đầu theo hướng của transform (`set_target_velocity`)
  trước khi giao cho SUT, để ego đã chạy đều ngay khi tình huống bắt đầu.
- `ego_vehicle.target_speed_kmh` (tuỳ chọn): runner giới hạn SUT ở tốc độ này THAY biển báo của map. Behavior Agent:
  ghi đè giới hạn tốc độ mà agent đọc mỗi tick (`_speed_limit`, mặc định lấy `vehicle.get_speed_limit()`) bằng giá trị
  này; Autopilot: `TrafficManager.set_desired_speed(ego, target)`. Không khai thì SUT chạy theo biển báo như cũ.
- `route.speed_limit_kmh` vẫn chỉ là ngưỡng của Oracle speeding, không áp cho SUT (S61 dựa vào điểm này để thử vượt tốc).
- Mẫu S25/S42/S52 gắn cả hai trường vào tham số `ego_speed_kmh` để quét tốc độ thử như lưới Euro NCAP.
- `lead_vehicle_brake.params.time_gap_s` (2026-10-21, tuỳ chọn, như OpenSCENARIO `LongitudinalDistanceAction` timeGap,
  Euro NCAP CCRb 2026 dùng 1.0 s): từ đầu run tới khi event kích hoạt, mỗi tick
  ```
  d_star = time_gap_s * v_ego                       # khoảng cách mong muốn (tâm–tâm hoặc mũi–đuôi, ghi rõ trong log)
  v_ref  = max(initial_speed_kmh, v_ego)
  v_cmd  = clamp(v_ego - 0.5 * (d - d_star), 0.7 * v_ref, 1.1 * v_ref)   # 0.5 1/s: hệ số bám
  # d = khoảng cách actor phía trước ego (dọc hướng ego). d > d_star: actor chậm hơn ego để ego bắt kịp.
  # (Sửa 2026-10-26: bản cũ ghi "+", làm actor xa thì chạy xa thêm, gần thì dừng lại; đáy cũ là 0 làm actor
  #  dừng chờ khi khoảng cách ban đầu lớn, ego dừng sau actor đứng yên -> kẹt. Đáy 70% giữ actor luôn chạy.)
  agent.set_target_speed(v_cmd * 3.6)
  ```
  Kích hoạt thì phanh với `deceleration_ms2` tới `target_speed_kmh`. Không khai thì actor chạy đều ở `initial_speed_kmh`.
  S41 dùng chung `test_speed_kmh` cho ego (initial + target) và lead_car, `time_gap_s` 1.0, phanh 4 m/s².
- Actor `behavior: scripted` có `initial_speed_kmh` > 0: runner cho chạy theo làn ở tốc độ đó từ đầu run cho tới khi một
  event điều khiển nó (S41 trước khi phanh, S43 xe chậm 20 km/h không có event nào).
- `pedestrian_crossing.params.sync` (2026-10-21, như `SynchronizeAction` Euro NCAP dùng cho người đi bộ): sau khi trigger
  kích hoạt, người đi bộ đứng chờ; mỗi tick tính `t_ego = d_ego / max(v_ego, 0.5)` tới `sync.point` và
  `t_ped = d_ped / speed_ms`; khi `t_ped >= t_ego + offset_s` thì bắt đầu đi ở `speed_ms` theo `direction`. Đã trễ ngay lúc
  kích hoạt thì đi luôn và log `sync_missed`. S25: point = tâm làn ego trước người đi bộ, offset ±0.5 s ≈ điểm va 10–90%.

Ý nghĩa từng khối, 5 loại event và 5 loại trigger: `docs/scenario-schema.md` mục 3. Hai trigger thêm 2026-10-17:
- `after_event`: chạy `delay_s` giây sau thời điểm event `event_ref` BẮT ĐẦU (không phải lúc kết thúc). Flow 1 đảm bảo
  event được chờ tồn tại và không có vòng; event được chờ không bao giờ chạy thì event chờ cũng không chạy.
- `time_to_collision`: mỗi tick tính TTC = khoảng cách ego–actor / tốc độ tiếp cận; chỉ xét khi tốc độ tiếp cận > 0;
  kích hoạt lần đầu TTC ≤ `ttc_s`. Đề xuất đo khoảng cách tâm–tâm (giống trigger khoảng cách), runner có thể trừ
  bán kính bao nếu muốn, nhưng phải ghi rõ.

Event thêm 2026-10-19:
- `vehicle_crossing`: actor (`behavior` scripted/idle) đứng yên tới khi trigger kích hoạt, rồi chạy tới
  `params.destination` (transform có trong `locations` với path `/events/{i}/params/destination` nếu khai bằng
  spawn index) với tốc độ đích `params.speed_kmh`. Gợi ý cài: `BasicAgent(vehicle, target_speed=speed_kmh)` +
  `set_destination(destination)`; `ignore_traffic_lights = true` thì `agent.ignore_traffic_lights(True)` (xe vượt đèn
  đỏ). Không tự né ego: mục đích là tạo xung đột ở nút giao để SUT phải nhường/phanh. Tới đích thì dừng (hoặc
  giao Traffic Manager), runner ghi rõ cách đã chọn.
- `vehicle_crossing.params.sync` (2026-10-19, tuỳ chọn, như OpenSCENARIO `SynchronizeAction` với ego là master):
  `point` = điểm xung đột (transform trong `locations` nếu khai bằng spawn index), `offset_s` (mặc định 0, đã giải
  `$param`). Có `sync` thì `speed_kmh` là tốc độ TỐI ĐA. Mỗi tick, cho tới khi actor qua `point`:
  ```
  d_ego   = khoảng cách ego → point (đường thẳng; dùng quãng theo route nếu có)
  t_ego   = d_ego / max(v_ego, 0.5)                    # ego dự kiến tới point sau t_ego giây
  t_actor = max(t_ego + offset_s, 0.1)                 # actor phải tới point sau t_actor giây
  v_cmd   = min(d_actor / t_actor, speed_kmh / 3.6)    # d_actor theo đường actor sẽ chạy (route planner)
  agent.set_target_speed(v_cmd * 3.6)
  ```
  Qua `point` (hoặc ego đã qua) thì chạy tiếp ở `speed_kmh` tới `destination`. Không đạt được vì giới hạn tốc độ thì
  actor tới muộn: runner ghi `sync_missed` cùng độ lệch thực tế vào log để Flow 3/4 biết tình huống không xảy ra
  như thiết kế. Offset âm = actor tới trước ego (đi cắt ngang/nhập làn ngay trước mặt), dương = tới sau.

## 6. SV4 – sinh biến thể / chạy lại

Dùng API sẵn có của B12 (`docs/variant-generation.md`):
`POST /api/v1/scenarios/{id}/versions/{n}/variant-batches` với `parameterGrid`. Mỗi tổ hợp tạo một scenario
riêng + draft đã validate (chưa publish). Sau khi publish, mỗi biến thể có `scenarioVersionId` riêng và xuất
hiện trong `GET /scenario-versions/ready`. `resolvedContent.source.parent_version_id` trỏ về version gốc.

## 7. SV5 – feature tĩnh cho ưu tiên scenario

Lấy từ `GET /scenario-versions/ready` (category, mapName, weatherPreset, npcVehicleCount, pedestrianCount,
eventTypes, eventCount, actorCount, speedLimitKmh) và `parameterValues` của gói chạy. Bộ thực nghiệm 12 biến
thể: `examples/experiments/SV5_pedestrian_grid.json` + `tools/create_variant_experiment.py` (manifest có
`scenarioVersionId` + `contentHash`).

## 8. Kiểm thử

`tools/test_handoff.py` (34 kiểm tra, 2026-10-08 đạt 34/34 trên Supabase thật ở `Auth:Mode=Dev`):
danh sách READY, `latestOnly`, nhiều version READY, gói chạy đã giải `$param`, toạ độ khớp
`map_metadata/*.json`, bản `_Opt`, version DEPRECATED và scenario ARCHIVED, 400/404.

```bash
ASX_API_BASE=http://localhost:5080 python3 tools/test_handoff.py
```

## 9. Còn cần chốt với SV2

| Câu hỏi | Đề xuất của Flow 1 |
|---|---|
| Runner đọc gói JSON này trực tiếp hay chuyển sang ScenarioRunner/OpenSCENARIO? | đọc trực tiếp gói (đơn giản, giữ nguyên hash) |
| Cách hiện thực 4 event type, 5 trigger trong runner | SV2 hiện thực, Flow 1 cung cấp ví dụ trong `examples/` |
| Campaign chạy version DEPRECATED được không? | chỉ khi chạy lại campaign cũ; campaign mới dùng `selectableForNewCampaign` |
| Runner tự kiểm phiên bản CARLA? | có, so với `map.carlaVersion` |

## 10. Oracle – thoả thuận với SV4/SV5 (đề xuất 2026-10-17, CHỜ Flow 3 duyệt)

**Ai giữ ngưỡng.** Scenario chỉ mô tả *điều kiện của thế giới*: `route.speed_limit_kmh` (biển tốc độ áp cho
scenario), `route.goal_tolerance_m` (bán kính coi là tới đích), `execution.timeout_s`. *Ngưỡng phán quyết* do
Admin (Flow 5) cấu hình: dung sai vượt tốc, TTC tối thiểu, số giây N cho stagnation… Oracle lấy giá trị thế giới
từ gói chạy, lấy ngưỡng từ cấu hình Admin; scenario không ghi đè ngưỡng.

**Loại oracle mà scenario chủ động tạo được.** Event của schema v1.0 nhắm tới 5 loại: collision
(`pedestrian_crossing`, `vehicle_cut_in`, `lead_vehicle_brake`, `vehicle_crossing`), TTC (cùng các event trên), red light
(`traffic_light_override`), speeding (`speed_limit_kmh`), route incomplete (`goal_tolerance_m` + `timeout_s`).
`lane_invasion` và `stagnation` không có event chủ đích (stagnation chỉ gián tiếp qua `static_obstacle` chắn
đường); bộ thực nghiệm tập trung vào 5 loại trên, vẫn đạt mức tối thiểu 5 loại của phiếu đăng ký.

**Tên loại lỗi (đề xuất, cần Flow 3 duyệt).** Flow 1 sẽ cho Scenario Engineer gắn nhãn "lỗi dự kiến" ở mức
scenario (giống tags, sửa được bất kỳ lúc nào, không thuộc content/hash) và gói chạy sẽ mang trường
`expectedViolations`. Flow 4 so nhãn này với lỗi oracle phát hiện (ground truth cho RQ1/APFD). Tên phải trùng
từng chữ với loại lỗi Flow 3 xuất ra:

| Tên | Ý nghĩa |
|---|---|
| `collision` | va chạm |
| `lane_invasion` | lấn làn |
| `ttc_violation` | TTC dưới ngưỡng an toàn |
| `speeding` | vượt tốc độ giới hạn |
| `red_light` | vượt đèn đỏ |
| `route_incomplete` | không hoàn thành route trước timeout |
| `stagnation` | xe đứng yên quá N giây |

Chưa cài đặt; Flow 1 chỉ code sau khi Flow 3 xác nhận danh sách.
