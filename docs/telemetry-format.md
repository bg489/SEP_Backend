# Telemetry `asx.telemetry/1`

Hợp đồng giữa runner (SV2, ghi) và Oracle (SV3, đọc). Thuật ngữ theo `CONTEXT.md`; lý do thiết kế ở
[ADR 0001](adr/0001-offline-versioned-oracle.md). File mẫu Oracle chấp nhận: `examples/telemetry/*.jsonl`.

## 1. Dạng file

- **JSON Lines**, UTF-8: mỗi dòng một object, phân loại bằng `"kind"`. Một file cho một Run.
- Thứ tự: `header` đầu tiên, sau đó `tick` và `event` theo thời gian mô phỏng, `end` cuối cùng.
- Thời gian `t` là giây mô phỏng tính từ khi Run bắt đầu. Toạ độ, vận tốc theo hệ toạ độ thế giới CARLA (m, m/s, độ).
- Khi lưu, file được nén gzip (việc của tầng lưu trữ; nội dung không đổi).

## 2. Bản ghi

### `header` (đúng một)

```json
{"kind":"header","format":"asx.telemetry/1","run_id":"…","scenario_version_id":"…","content_hash":"sha256:…",
 "carla_version":"0.9.16","sut":{"agent":"behavior","profile":"normal"},"seed":42,"fixed_delta_seconds":0.05,
 "ego_extent":{"x":2.4,"y":1.0}}
```

Trường header tùy chọn Oracle đọc (sai kiểu → ERROR): `goal {x,y}` (Route completion, Freezing), `warmup_s` (Freezing bỏ qua đoạn đầu), `route_speed_limit_kmh` (Speeding ưu tiên hơn `ego.speed_limit_kmh` khi có). Thiếu `goal` thì không xét Route completion cho Run COMPLETED.

`format` khác `asx.telemetry/1` → Oracle trả ERROR. Đổi không tương thích thì tăng lên `asx.telemetry/2`.
`ego_extent`: nửa chiều dài (`x`) và nửa chiều rộng (`y`) bounding box của ego (m), không đổi trong Run.

### `tick` (mỗi tick mô phỏng, không lấy mẫu thưa)

```json
{"kind":"tick","t":1.25,"post_stop":false,
 "ego":{"x":0,"y":0,"z":0.5,"yaw":0,"pitch":0,"roll":0,"vx":5,"vy":0,"ax":0,"ay":0,
        "throttle":0.4,"brake":0,"steer":0,"speed_limit_kmh":50,"road_id":12,"lane_id":-1},
 "traffic_light":"red",
 "actors":[{"id":101,"type":"vehicle","x":30,"y":0,"yaw":0,"vx":5,"vy":0,"road_id":12,"lane_id":-1,
            "extent":{"x":2.4,"y":1.0}}]}
```

| Trường | Ý nghĩa |
|---|---|
| `ego.speed_limit_kmh` | giới hạn tốc độ của đường tại vị trí ego (`vehicle.get_speed_limit()`), dùng cho Speeding khi `route.speed_limit_kmh` để trống |
| `traffic_light` | trạng thái đèn ego đang đối mặt: `red`, `yellow`, `green`, `none` |
| `actors` | **mọi** actor trong bán kính 50 m quanh ego; `type` là `vehicle`, `walker` hoặc `static` |
| `road_id`, `lane_id` | làn của ego/actor theo waypoint CARLA (`Map.get_waypoint`), dùng xét "cùng làn với ego" (Freezing) |
| `actors[].extent` | nửa kích thước bounding box (m), dùng tính khoảng cách va chạm cho TTC |
| `post_stop` | `true` cho đoạn đuôi 1 s ghi tiếp sau khi Run dừng; Oracle bỏ qua, Flow 4 dùng |

### `event` (luồng sensor)

| `type` | Trường thêm |
|---|---|
| `collision` | `other_actor {id, type}`, `impulse` (N·s), `impact_side` (`front`, `rear`, `left`, `right`) |
| `lane_invasion` | `markings`: các loại vạch bị cắt, đúng tên `carla.LaneMarkingType`: `Broken`, `Solid`, `SolidSolid`, `SolidBroken`, `BrokenSolid`, `BrokenBroken`, `BottsDots`, `Grass`, `Curb`, `Other`, `NONE` |
| `red_light` | `light_id`: ego vượt vạch dừng khi đèn đỏ (kể cả đèn bị event `traffic_light_override` ép đỏ) |

Mọi event đều có `t` và `post_stop`.

### `end` (đúng một, dòng cuối)

```json
{"kind":"end","t":14.2,"outcome":"STOPPED_ON_VIOLATION"}
{"kind":"end","t":3.1,"outcome":"INFRA_FAILED","reason":"CARLA server stopped responding"}
```

`outcome` là Run Outcome: `COMPLETED`, `TIMED_OUT`, `STOPPED_ON_VIOLATION`, `INFRA_FAILED` (bắt buộc có `reason`, tiếng Anh). Giá trị khác → Oracle trả ERROR.

## 3. Quy tắc cho runner

- Chỉ dừng Run khi có Collision hoặc Red-light running, rồi ghi tiếp 1 s với `post_stop: true`, sau đó ghi `end`.
- Không có `end` → Oracle coi file bị cắt ngang và trả ERROR. Dòng giữa file không phải JSON object, hoặc `kind`, `format`, `type`, `outcome`, `reason` không phải chuỗi, `t` không phải số, `post_stop` không phải true/false → cũng ERROR, kèm số dòng. Hạ tầng hỏng thì vẫn cố ghi `end` với `INFRA_FAILED`.
- Ghi dữ liệu thô, không tự lọc "actor liên quan": việc chọn thuộc về Oracle để còn chấm lại được.

## 4. Ghi Telemetry thật để thử Oracle

`tools/carla_recorder.py` là công cụ dev của Flow 3, **không phải** runner Flow 2: đọc một gói chạy đã lưu,
dựng scenario trong CARLA, cho BehaviorAgent lái ego và ghi `recordings/*.jsonl` (đã gitignore).

1. Cài Python 3.12 (wheel `carla==0.9.16` chưa có cho bản mới hơn), tạo venv, `pip install carla==0.9.16`.
2. Wheel **không** có gói `agents`: đặt `CARLA_ROOT` tới thư mục CARLA 0.9.16 đã tải, rồi
   `pip install -r %CARLA_ROOT%\PythonAPI\carla\requirements.txt`.
3. Chạy server: `%CARLA_ROOT%\CarlaUE4.exe -quality-level=Low -windowed -ResX=1280 -ResY=720` (GPU 4 GB chỉ chịu được mức Low).
   Camera của cửa sổ tự bám sau ego khi recorder chạy; thêm `-RenderOffScreen` nếu không cần xem (nhẹ GPU hơn).
4. Lưu gói chạy: `GET /api/v1/scenario-versions/{id}/execution-package` → `package.json`.
5. `python tools/carla_recorder.py package.json --profile normal` (`cautious`, `normal`, `aggressive`).
6. Kiểm bản ghi bằng chính Oracle (đường dẫn tuyệt đối, vì test chạy trong `bin/`; không đặt biến thì test tự bỏ qua):
   `set ASX_TELEMETRY_DIR=%CD%\recordings` rồi `dotnet test backend --filter "FullyQualifiedName~Real_carla"`.

Kết thúc Run: tới đích trong `route.goal_tolerance_m` → `COMPLETED`; hết `execution.timeout_s` (tính sau warmup) → `TIMED_OUT`;
Collision → ghi thêm 1 s rồi `STOPPED_ON_VIOLATION`; exception hoặc Ctrl+C → `INFRA_FAILED`. Trong `warmup_s`
ego bị phanh, tick vẫn được ghi từ `t = 0`; trigger `simulation_time` cũng tính sau warmup. Lỗi trước khi Run
bắt đầu (nạp map, spawn, lập route) không tạo file, chỉ in lỗi và thoát.

Chưa hiện thực: event `vehicle_cut_in`, `traffic_light_override` (bỏ qua kèm cảnh báo), event `red_light`
(làm cùng Check Red-light, tham khảo `RunningRedLightTest` của ScenarioRunner), SUT Autopilot.
