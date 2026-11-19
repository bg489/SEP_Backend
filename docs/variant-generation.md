# B12 – Sinh biến thể tham số

Một batch lấy **version READY của scenario ACTIVE** làm gốc. Mỗi tổ hợp tạo một scenario
mới (`origin_type = variant`) và một draft đã validate. Kỹ sư publish từng draft bằng API
hiện có để nhận `scenario_version_id` READY. Không sửa/deprecate version gốc khi sinh batch.
Dùng `variant_batches`, `scenarios`, `validation_reports`, `scenario_drafts` và `audit_logs`;
migration `105_variant_batch_strategy.sql` (2026-10-09) thêm cột `variant_batches.strategy`.

## API

Các endpoint đều yêu cầu policy `ManageScenarios` (SCENARIO_ENGINEER hoặc ADMIN).

| Method | Đường dẫn | Kết quả |
|---|---|---|
| POST | `/api/v1/scenarios/{id}/versions/{n}/variant-batches` | 201, batch + danh sách scenario, header Location |
| GET | `/api/v1/scenarios/{id}/versions/{n}/variant-batches` | Lịch sử batch từ version, mới nhất trước |
| GET | `/api/v1/scenarios/{id}/versions/{n}/variant-batches/{batchId}` | Grid gốc, scenario IDs, trạng thái draft/publish hiện tại |

Ví dụ body:

```json
{
  "namePrefix": "Pedestrian sweep",
  "note": "3 khoảng cách × 2 tốc độ",
  "strategy": "cartesian",
  "parameterGrid": {
    "trigger_distance_m": [5, 15, 25],
    "pedestrian_speed_ms": [0.8, 2.0]
  }
}
```

Kết quả tạo 6 scenario, tên kết thúc bằng `[variant 001]` đến `[variant 006]`.
`namePrefix` tùy chọn, từ 1 đến 100 ký tự; mặc định lấy tối đa 100 ký tự của tên gốc.
`note` tối đa 1000 ký tự. `strategy` tùy chọn, mặc định `cartesian` (xem mục dưới).
Response của batch có thêm `strategy` và `cartesianCount` (số tổ hợp nếu sinh đủ grid).

## Chiến lược tổ hợp (2026-10-09)

| `strategy` | Sinh ra | Khi nào dùng |
|---|---|---|
| `cartesian` (mặc định) | Mọi tổ hợp, trục đầu (ordinal) đổi chậm nhất | Grid nhỏ, cần đủ mọi tổ hợp |
| `pairwise` | Bộ phủ **mọi cặp giá trị của hai tham số bất kỳ** | Từ 3 tham số trở lên, grid lớn |

Pairwise dựa trên giả thuyết đã được kiểm chứng trong kiểm thử tổ hợp: phần lớn lỗi do tương tác
của tối đa hai tham số gây ra, nên phủ đủ các cặp giữ được phần lớn khả năng phát hiện lỗi với số
scenario ít hơn nhiều. Cài đặt là IPOG (Lei & Tai) với t = 2 trong `ParameterGridExpander`:

1. Xếp tham số theo số giá trị giảm dần (hoà giữ thứ tự ordinal), khởi tạo bằng tích của hai trục đầu.
2. Mỗi tham số tiếp theo: **mở rộng ngang** – mỗi dòng chọn giá trị phủ nhiều cặp chưa phủ nhất
   (hoà chọn chỉ số nhỏ nhất); **mở rộng dọc** – cặp còn thiếu được điền vào ô trống của dòng có
   sẵn hoặc thêm dòng mới.
3. Ô còn trống nhận giá trị đầu tiên của trục.

Thuật toán **tất định**: cùng grid luôn ra cùng tập tổ hợp và cùng thứ tự, nên batch tái lập được
và `content_hash` của từng biến thể ổn định. Với 1–2 tham số, pairwise trùng cartesian.
Số tổ hợp không nhỏ hơn tích của hai trục dài nhất (cận dưới); IPOG không luôn đạt tối ưu:

| Grid | Cartesian | Pairwise (IPOG) | Cận dưới |
|---|---|---|---|
| SV5: 3 × 2 × 2 | 12 | 6 | 6 |
| 2 × 2 × 2 × 2 | 16 | 6 | 4 |
| 3 × 3 × 3 × 3 | 81 | 10 | 9 |
| 5 × 3 × 2 × 2 | 60 | 16 | 15 |
| 5 × 5 × 5 × 5 | 625 (vượt giới hạn 100) | 32 | 25 |
| 3 × 3 × 3 × 3 × 3 × 3 | 729 (vượt giới hạn 100) | 16 | 9 |
| 4 × 4 × 4 × 4 × 4 | 1024 (vượt giới hạn 100) | 16 | 16 |

Giới hạn `MaxVariantsPerBatch` áp dụng cho **số tổ hợp sinh ra**: với pairwise, grid có tích
Descartes vượt giới hạn vẫn được nhận nếu kết quả pairwise nằm trong giới hạn (kiểm tra cận
dưới trước khi sinh, rồi kiểm tra kết quả). `requested_count`/`generated_count` là số tổ hợp
sinh ra theo strategy. Strategy khác hai giá trị trên trả 400 với field `strategy`.
UI (tab Variants) có ô "Combination strategy"; khi chọn pairwise với ≥ 3 tham số, ô xem trước
hiện cận dưới và kích thước grid đủ, số chính xác hiện trong lịch sử batch sau khi sinh.

## Quy tắc

- Grid phải chứa ít nhất một tham số đã khai báo và mỗi danh sách phải có ít nhất một số.
  Giá trị trong `[min,max]`; kiểu `integer` không nhận phần thập phân. Giá trị được kiểm tra
  trong phạm vi số decimal của .NET. Không nhận chuỗi số, boolean, null hoặc giá trị trùng
  trong một danh sách (1 và 1.0 cũng được coi là trùng).
- Trục thời tiết (2026-10-21): khoá `weather.preset` nhận danh sách preset trong catalog (không trùng), ví dụ
  `{"weather.preset": ["ClearNoon", "ClearNight"], "ego_speed_kmh": [10, 20]}` như Euro NCAP thử người đi bộ ngày/đêm.
  Mỗi tổ hợp đổi `weather.preset`, giữ các giá trị ghi đè (mưa, sương…) của scenario gốc. Khoá có dấu chấm nên không
  trùng tên tham số; `parameter_grid` là jsonb nên không cần migration. UI: nhóm "Weather presets" ở tab Variants.
- Sinh theo `strategy`, giới hạn `Scenario:MaxVariantsPerBatch` (mặc định 100). Với cartesian,
  kiểm tra số tổ hợp trước khi cấp phát để tránh tràn số hoặc tạo quá nhiều dữ liệu.
- Thứ tự tên tham số là ordinal; thứ tự các giá trị giữ nguyên như request. Chỉ đổi
  `parameters.<name>.value`, metadata tên/hồ sơ và source; giữ các `$param`, miền min/max,
  `step`, seed, map, route, weather, traffic, actor và event. `step` là gợi ý chọn giá trị,
  không bắt buộc giá trị nhập phải nằm trên lưới step.
- `source.type = variant`, `source.parent_version_id` và `scenarios.origin_version_id`
  trỏ tới version gốc. `scenarios.variant_batch_id` trỏ tới batch. JSON Schema v1.0
  được mở rộng thêm loại nguồn `variant`, bắt buộc `parent_version_id` như `clone`;
  tài liệu v1.0 cũ vẫn hợp lệ.
- Mỗi tổ hợp chạy validator 3 tầng; đồng thời kiểm tra bản tạm đã giải `$param` để bắt
  miền/kiểu của trường sử dụng tham số (ví dụ tốc độ đi bộ quá lớn dù vẫn trong miền
  khai báo của tham số). Bản lưu giữ nguyên tham chiếu. Khi chưa có map metadata,
  validation vẫn có cảnh báo CARLA như luồng hiện có; READY không có nghĩa đã chạy CARLA.
- Nếu bất kỳ tổ hợp nào sai, trả 422 `VARIANT_VALIDATION_FAILED`, kèm `variantIndex`
  (tính từ 1), `parameterValues` và `report` (`stoppedAt`, `errors`, `warnings`). Không
  lưu batch hoặc scenario nào. Nếu tất cả hợp lệ, lưu batch, scenario, report, draft và
  audit trong **một transaction**.
- Sau khi tạo, đọc `GET /api/v1/scenarios/{variantId}/draft` để xem nội dung/report ID,
  rồi `POST /api/v1/scenarios/{variantId}/versions` với `{}` để publish. Nếu sửa draft,
  phải validate lại trước khi publish theo quy tắc hiện có.
- Lịch sử vẫn đọc được sau khi archive scenario gốc hoặc deprecate version gốc.
  POST không có idempotency key: gửi lại request thành công sẽ tạo batch mới.

Lỗi request/grid trả 400 `INVALID_REQUEST`; scenario/version/batch không tồn tại trả 404;
nguồn archive trả 422 `SCENARIO_ARCHIVED`, version không READY trả 422 `VARIANT_BASE_NOT_READY`.

## Bộ thực nghiệm SV5

1. Import `examples/S23_pedestrian_crossing_rain.json`, publish draft của scenario vừa tạo.
2. Dùng `examples/experiments/SV5_pedestrian_grid.json`: 3 khoảng cách (5/15/25 m),
   2 tốc độ đi bộ (0.8/2.0 m/s), 2 mật độ NPC (0/40), tổng **12 biến thể**, cùng seed 42.
3. Có thể thực hiện bằng Swagger, hoặc dùng công cụ để sinh, publish và xuất JSON:

```bash
ASX_API_BASE=http://localhost:5080 python3 tools/create_variant_experiment.py \
  --scenario-id <id-cua-scenario-S23> --version 1 --output /tmp/sv5-pedestrian-run
```

`--output` phải là thư mục mới. Script giữ các version READY trong DB và xuất từng file
JSON nguyên văn từ API cùng `manifest.json` chứa batch, scenario/version IDs, hash, seed,
giá trị tham số và tên file. Manifest được lưu từng bước để tra cứu nếu bị ngắt giữa chừng;
không có transaction bao trùm bước publish nhiều scenario. Dùng `ASX_TOKEN` nếu chạy
Supabase Auth; script không lưu token vào manifest. Với HTTPS, cần chứng chỉ đã được tin cậy.

### Bộ đầy đủ 5 loại oracle (2026-10-17, 57 biến thể)

Mỗi dòng: import + publish file gốc, rồi chạy script trên với `--grid` tương ứng (mỗi grid một thư mục `--output`).
Lỗi dự kiến là giả thuyết thiết kế, chỉ đúng sau khi chạy CARLA; toạ độ đã kiểm tầng 3 với `map_metadata/`.

| File gốc | Grid | Biến thể | Lỗi dự kiến (ca nặng) | Ca dự kiến đạt |
|---|---|---|---|---|
| `S23_pedestrian_crossing_rain.json` (Town05) | `SV5_pedestrian_grid.json` | 12 | collision, ttc_violation | trigger 25 m, đi chậm |
| `S31_cut_in_imported.json` (Town04) | `SV5_cut_in_grid.json` | 15 | collision, ttc_violation | gap 20–30 m, phanh 3 m/s² |
| `S41_lead_vehicle_brake.json` (Town04) | `SV5_lead_brake_grid.json` | 12 | collision, ttc_violation | phanh 3 m/s² |
| `S51_red_light_intersection.json` (Town03) | `SV5_red_light_grid.json` | 12 | red_light khi đỏ lúc còn 10 m tới vạch; route_incomplete khi đỏ 60 s | đỏ từ ≥ 20 m, giữ 10 s |
| `S61_speeding_open_road.json` (Town04) | `SV5_speeding_grid.json` | 6 | speeding (giới hạn 60 < biển báo map) | NPC dày |

Mọi tổ hợp đã chạy qua `tools/validate_scenario.py` (0 lỗi, 0 cảnh báo). Chưa xác minh trên CARLA: route S51 có đi qua
nút đèn không, xe dẫn đầu S41 (spawn 100, cách 42 m cùng làn) có đúng làn ego không, và Behavior Agent có thật sự chạy
theo biển báo map ở S61 không. `lane_invasion`/`stagnation` không có grid riêng (xem `docs/handoff-flow2.md` mục 10).

S31 và S41 cũng kích hoạt theo vị trí của ego (`ego_distance_to_location` với `spawn_point_index` làm điểm mốc), không theo
giây và không theo khoảng cách tới actor: S41 lead_car có thể nhanh hơn ego nên khoảng cách không thu hẹp; S31 npc_cutter
xuất phát cách ego ~4 m nên trigger theo actor sẽ bắn ngay. S31: start #7, npc_cutter #6 (làn trái, khớp `side: left`),
chen khi ego còn 45 m tới #205, phanh khi còn 5 m (đoạn thẳng có spawn point xác nhận chỉ dài ~102 m). S41: phanh khi ego
cách #134 dưới 20 m (~219 m sau start); từ 2026-10-21 theo Euro NCAP CCRb 2026: `test_speed_kmh` chung cho hai xe,
runner giữ lead_car cách ego `time_gap_s` (1.0 s), grid SV5 đổi `lead_speed_kmh` → `test_speed_kmh`.

S51 kích hoạt đèn đỏ theo **khoảng cách tới vạch dừng** (`ego_distance_to_location`, tham số `red_distance_m`), không theo
giây: thời điểm ego tới ngã tư phụ thuộc traffic và cách Behavior Agent tăng tốc, nên trigger theo giờ có thể chuyển đỏ
khi ego còn ở xa hoặc đã qua ngã tư. Cùng cách các bộ kịch bản CARLA ScenarioRunner/Leaderboard và điều kiện
`ReachPositionCondition` của OpenSCENARIO (đổi 2026-10-17, trước đó là `red_time_s`).

Đây là bộ đầu vào có thể tái lập cho SV5. Cần B6 và CARLA để kiểm chứng tọa độ/khả năng chạy,
và kết quả oracle từ Flow 2–3 để tính APFD/APFDc; chưa có kết quả mô phỏng trong B12.

## Kiểm thử

```bash
ASX_API_BASE=http://localhost:5080 python3 tools/test_variants.py
```

Chỉ cần Python 3.9+ chuẩn, API đang chạy và DB đã seed user dev (hoặc token Supabase).
Test tạo scenario riêng, kiểm tra đủ 12 tổ hợp, hash, publish, export, lịch sử, lỗi grid,
giới hạn số lượng, batch có tổ hợp lỗi, nguồn không READY/archive và script xuất thực nghiệm.
Cuối cùng archive các scenario test; dữ liệu vẫn được giữ trong DB để audit.

Kết quả ngày 2026-09-22: **234/234 kiểm tra đạt** trên Supabase ở Auth:Mode=Dev,
16 scenario test của lần chạy cuối đã archive. `dotnet build --no-restore`: 0 lỗi, 0 cảnh báo.
Hồi quy B11 Import/Export: 24/24 đạt; 4 scenario test đã archive.

Kết quả ngày 2026-10-09 (sau B12b pairwise + migration 105): **243/243 kiểm tra đạt**, gồm 11 ca
pairwise (phủ đủ cặp, tất định, grid vượt giới hạn, strategy lạ); 56 scenario test đã archive.
