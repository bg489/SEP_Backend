# Đối chiếu mẫu scenario với danh mục tình huống chuẩn (2026-10-18)

Mục đích: trả lời câu hỏi "scenario lấy từ đâu, phủ được bao nhiêu tình huống thực tế". Mỗi mẫu tạo scenario
(`GET /api/v1/reference/scenario-templates`, trường `reference`) và ví dụ trong `examples/` được gắn với tình huống
tương ứng trong danh mục của NHTSA, Euro NCAP và CARLA Leaderboard.

## 1. Nguồn

- **NHTSA pre-crash scenario typology** – W. G. Najm, J. D. Smith, M. Yanagisawa, *Pre-Crash Scenario Typology for Crash
  Avoidance Research*, DOT HS 810 767, NHTSA, 04/2007. Phân tích cơ sở dữ liệu tai nạn GES 2004 của Mỹ, gom thành
  **37 kiểu tình huống trước va chạm**, chiếm khoảng 5.942.000 vụ tai nạn có xe nhẹ do cảnh sát ghi nhận. Tỉ lệ dưới đây
  lấy từ *Table 13. Pre-Crash Scenarios of All Light-Vehicle Crashes*; số thứ tự `#` là số của kiểu trong danh sách 37.
  Theo tài liệu thứ cấp, bản cập nhật 2019 còn 36 kiểu chia 9 nhóm; tài liệu này dùng bản 2007 vì là bản CARLA
  Leaderboard trích dẫn.
- **Euro NCAP AEB** – bài thử phanh khẩn cấp: Car-to-Car Rear Stationary/Moving/Braking (CCRs/CCRm/CCRb), rẽ cắt
  ngang (CCFtap), cắt ngang thẳng (CCCscp); người đi bộ Car-to-Pedestrian Nearside/Farside Adult (CPNA/CPFA). Từ protocol
  AEB/LSS VRU v4.5 (12/2023) đổi tên thành CVNA/CVFA, nội dung tương đương.
- **CARLA Leaderboard traffic scenarios** – 21 tình huống "selected from the NHTSA pre-crash typology"
  (leaderboard.carla.org/scenarios).

## 2. Mẫu hiện có ↔ danh mục chuẩn

| Mẫu (key) | File | NHTSA | Tỉ lệ vụ | Euro NCAP | CARLA Leaderboard |
|---|---|---|---|---|---|
| Stopped vehicle ahead (`stopped_vehicle`) | S42 | #26 Lead Vehicle Stopped | 16,41% (hạng 1) | CCRs | 12 Obstacle in lane (gần đúng) |
| Lead vehicle brakes (`lead_vehicle_brake`) | S41 | #25 Lead Vehicle Decelerating | 7,20% (hạng 4) | CCRb | 16 Longitudinal control after leading vehicle's brake |
| Cut-in then hard brake (`cut_in_brake`) | S31 | #18 Vehicle(s) Changing Lanes – Same Direction, sau đó #25 | 5,69% (hạng 6) | – | 8 Highway cut-in, 9 Static cut-in (gần đúng) |
| Slow vehicle ahead (`slow_vehicle`, 2026-10-21) | S43 | #24 Lead Vehicle Moving at Lower Constant Speed | chưa tra (Table 13) | CCRm | – |
| Car crosses the junction (`crossing_vehicle`, 2026-10-19) | S52 | #30 Straight Crossing Paths at Non-Signalized Junctions; #4 Running Red Light khi nút có đèn | 4,44% (hạng 8) | CCCscp | 04 Crossing negotiation, 05 Crossing traffic running a red light |
| Left turn across oncoming car (`left_turn_oncoming`, 2026-10-19) | S53 | #27 LTAP/OD at Signalized Junctions, #29 LTAP/OD at Non-Signalized Junctions | 3,71% + 3,19% (hạng 11, 13) | CCFtap | 02 Unprotected left turn at intersection with oncoming traffic |
| Car turns into the ego lane (`turning_into_path`, 2026-10-19) | S54 | #31 Vehicle(s) Turning at Non-Signalized Junctions (dạng Right Turn Into Path) | 7,32% (hạng 3) | – | – (LB 03 là ego rẽ phải, ngược vai) |
| Car in next lane turns across (`same_direction_turn`, 2026-10-19) | S55 | #16 Vehicle(s) Turning – Same Direction | 3,73% (hạng 10) | – | – |
| Red light at intersection (`red_light`) | S51 | #4 Running Red Light | 4,27% (hạng 9) | – | – (LB 05 là chiều ngược lại: xe khác vượt đèn đỏ) |
| Pedestrian steps out (`pedestrian_crossing`) | S25 (cùng S23, S24) | #12 Pedestrian Crash Without Prior Vehicle Maneuver | 0,66% | CPNA (người lớn băng từ lề phải) | 18 Pedestrian emerging from behind parked vehicle (gần đúng) |
| Open road speed limit (`open_road`) | S61 | – (chạy nền, không có tình huống va chạm chủ đích) | – | – | – |

Ghi chú:
- S51 đổi đèn của ego sang đỏ, kiểm xem SUT có dừng hay vượt đèn; NHTSA #4 tính cả hai phía (xe vượt đèn là ego hoặc
  xe khác) nên khớp phần ego vượt đèn.
- S23/S25 cho người đi bộ băng từ lề phải sang trái (`right_to_left`), ở nước đi bên phải đó là phía gần (nearside).
  S24 băng từ trái sang (`left_to_right`) ứng với CPFA.
- Ở #12 tỉ lệ vụ thấp nhưng mức thương vong cao; Euro NCAP chấm riêng người đi bộ vì lý do này.

**Độ phủ:** 9 mẫu có tình huống phủ 10 kiểu NHTSA (#4, #12, #16, #18, #25, #26, #27, #29, #30, #31), cộng lại
**≈ 56,6% số vụ tai nạn xe nhẹ** (16,41 + 7,32 + 7,20 + 5,69 + 4,44 + 4,27 + 3,73 + 3,71 + 3,19 + 0,66), gồm 7 trong 10
kiểu phổ biến nhất. 3 kiểu còn lại của top 10 hoặc ngoài phạm vi (#3 mất lái, #10 động vật) hoặc không cần tình huống
chủ đích (#7 lệch khỏi đường, oracle lane invasion bắt ở mọi scenario) – xem mục 3.

## 3. 10 kiểu phổ biến nhất của NHTSA và tình trạng

| Hạng | NHTSA | Tỉ lệ | Tình trạng | Cần gì để làm |
|---|---|---|---|---|
| 1 | #26 Lead Vehicle Stopped | 16,41% | ✅ S42 | – |
| 2 | #3 Control Loss Without Prior Vehicle Action | 8,90% | ❌ | Tham số ma sát mặt đường/hỏng xe; schema chưa có, ngoài phạm vi Flow 1 |
| 3 | #31 Vehicle(s) Turning at Non-Signalized Junctions | 7,32% | ✅ S54 | – |
| 4 | #25 Lead Vehicle Decelerating | 7,20% | ✅ S41 | – |
| 5 | #7 Road Edge Departure Without Prior Vehicle Maneuver | 5,62% | 🟡 | Không cần tình huống chủ đích: oracle lane invasion bắt ở mọi scenario (S61) |
| 6 | #18 Vehicle(s) Changing Lanes – Same Direction | 5,69% | ✅ S31 | – |
| 7 | #10 Animal Crash Without Prior Vehicle Maneuver | 5,13% | ❌ | CARLA không có sẵn blueprint động vật; thay tạm bằng `static_obstacle` thì là tình huống khác |
| 8 | #30 Straight Crossing Paths at Non-Signalized Junctions | 4,44% | ✅ S52 | – |
| 9 | #4 Running Red Light | 4,27% | ✅ S51 | – |
| 10 | #16 Vehicle(s) Turning – Same Direction | 3,73% | ✅ S55 | – |

Ngoài top 10 có #27/#29 LTAP/OD (rẽ trái cắt ngang xe ngược chiều, 3,71% + 3,19%): ✅ S53. Ego là SUT tự lái theo
route nên chọn start/goal để route buộc rẽ trái tại nút (start #138 làn trong hướng tây, goal #36 trên nhánh nam),
xe ngược chiều `vehicle_crossing` không vượt đèn vì cùng pha đèn với ego. Mọi vị trí chỉ suy từ spawn point; runner
Flow 2 cài hành vi theo `docs/handoff-flow2.md`.

## 4. Cách dùng trong báo cáo

- Câu tóm tắt: "Thư viện mẫu của AutoScenarioX được chọn theo NHTSA pre-crash typology; 9 mẫu tình huống phủ 10 kiểu
  chiếm ~57% số vụ tai nạn xe nhẹ (GES 2004), gồm kiểu phổ biến nhất (Lead Vehicle Stopped, 16,4%)."
- Bảng mục 2 làm bảng truy vết scenario ↔ chuẩn; bảng mục 3 làm phần giới hạn và hướng phát triển.
- Mọi vị trí trong mẫu mới suy từ spawn point của `map_metadata`, **chưa kiểm chứng trên CARLA**.

## 5. Chế độ Quick setup và lưới thử chuẩn (2026-10-20)

Cách các dự án cùng loại cho người dùng chọn kịch bản, đã tra từ nguồn gốc:

- **PEGASUS / ISO 34502**: kịch bản có 3 tầng. *Functional* là mô tả bằng lời. *Logical* là khoảng giá trị của vài tham
  số quan trọng. *Concrete* là một bộ giá trị cụ thể, do máy sinh. Ở hệ thống này: mẫu = functional, khối `parameters`
  (min/max/step) = logical, variant batch = concrete.
- **CARLA Leaderboard 2.0** (`leaderboard/data/routes_devtest.xml`, nhánh leaderboard-2.0): 25 loại tình huống viết sẵn
  thành class. Mỗi loại chỉ mở 0–4 tham số trong route XML (vd `DynamicObjectCrossing`: distance, blocker_model,
  crossing_angle; `PedestrianCrossing`, `HardBreakRoute`: không có tham số nào). Người dùng chọn tình huống và điểm
  kích hoạt, không cấu hình actor từ đầu.
- **Euro NCAP**: protocol chốt sẵn lưới thử. Mọi lần chạy bắt đầu ở TTC 4 s và đồng bộ đối tượng sao cho va chạm xảy ra
  nếu xe không phanh (VRU v4.5.1 mục 4.2.1).

Lưới thử hiện trong Quick setup theo category (frontend cũ `frontend/src/features/draft-editor/QuickSetup.tsx`, giữ ở nhánh local `frontend-legacy`), cập nhật
2026-10-21 theo **Euro NCAP Crash Avoidance – Frontal Collisions v1.2 (07/2026)**, bộ protocol 2026–2028
(https://cdn.euroncap.com/cars/assets/Euro_NCAP_Protocol_Crash_Avoidance_Frontal_Collisions_v1_2_220d87e28a.pdf):

| Category | Bài thử | Lưới 2026 | Mục | Bản 2024 (VRU v4.5.1, SA v10.4.1) |
|---|---|---|---|---|
| pedestrian | CPNA / CPFA | Xe 10–60 km/h bước **10**, thử cả ngày và đêm; người lớn đi 5 km/h từ phía gần hoặc chạy 8 km/h từ phía xa; điểm va 10/25/50/75/90% bề rộng xe | 3.2.3.1 | bước 5, điểm va 25/50/75% |
| braking | CCRb | Hai xe **cùng tốc độ 30–130 km/h** bước 10, cách nhau **1.0 s**, xe trước phanh **4 m/s²** | 3.1.1.1 | 50 km/h, cách 12/40 m, phanh 2/6 m/s² |
| obstacle | CCRs | Xe 10–80 km/h bước **10** tới xe đứng yên (AEB 10–50, FCW 60–80), độ chồng −25% … 125% | 3.1.1.1 | bước 5 |
| following | CCRm | Xe 30–80 km/h sau xe 20 km/h; 90–130 km/h sau xe chậm hơn 60 km/h | 3.1.1.1 | 30–80 km/h bước 5, xe trước 20 km/h |
| intersection | CCCscp, CCFtap | CCCscp: xe 20–80 × xe cắt ngang 20–80 km/h bước 10 (bỏ cặp quá nhanh), đồng bộ để va giữa xe, có 3 xe đỗ che khuất (Standard Range). CCFtap: xe rẽ 10/15/20/**25** × xe ngược chiều 30/45/60/**80** km/h (16 tổ hợp) | 3.1.2.1, 3.1.3.1 | CCCscp 20–60 + xuất phát đứng yên (30 tổ hợp); CCFtap 3 × 3 |

Mẫu S25 và S42 đổi bước `ego_speed_kmh` từ 5 sang 10 cho khớp lưới 2026. S41 theo CCRb 2026 (2026-10-21):
`test_speed_kmh` 30–130 chung cho hai xe, `lead_vehicle_brake.params.time_gap_s` 1.0 s, phanh 4 m/s². S52 giữ 20–60 km/h (70–80 km/h của
CCCscp chỉ ghép với xe cắt ngang 20–30 km/h).

Điểm khác của bản 2026 đáng ghi vào báo cáo:
- **Standard Range / Extended Range**: mỗi bài là một ma trận ô (grid cell); hãng xe dự đoán kết quả từng ô, phòng
  thử chọn ngẫu nhiên 3–5 ô Standard + 2 ô Extended để kiểm chứng (mục 3, 4.2.1). Tương tự variant batch + chọn mẫu.
- **Robustness layers** (mục 5.2.1): thêm sai lệch nhỏ vào tốc độ, gia tốc, vị trí ban đầu, hướng của mục tiêu để thử
  độ bền. Ý tưởng gần với sinh biến thể quanh một giá trị danh nghĩa.
- **Virtual Testing** (mục 4.1.1): Euro NCAP nhận dự đoán bằng mô phỏng (theo VTA Protocol) cho cả dải Standard/Extended
  và các corner case không thử được trên sân (CCRb > 100 km/h, CCFtap/CCCscp mục tiêu > 60 km/h…). Căn cứ cho hướng
  kiểm thử bằng mô phỏng của dự án.
- **Đồng bộ đường đi**: CCFtap/CCCscp đồng bộ để va đúng 50% bề rộng xe nếu không phanh, sai số thời gian ±0.1 s,
  giống `vehicle_crossing.params.sync`.
- Protocol dẫn tới bộ scenario OpenSCENARIO XML của Vector cho các bài Euro NCAP:
  https://github.com/vectorgrp/OSC-NCAP-scenarios (tham khảo khi làm adapter OpenSCENARIO).

Ghi chú: SUT là Behavior Agent, tốc độ thử đặt qua `ego_vehicle.initial_speed_kmh` + `target_speed_kmh` (runner giữ SUT
ở tốc độ này). Lưới trên là căn cứ để chọn khoảng tham số, không phải bài thử Euro NCAP chạy nguyên văn.

Từ 2026-10-20 ba mẫu quét được tốc độ thử của ego như Euro NCAP, qua tham số `ego_speed_kmh` gắn vào cả
`ego_vehicle.initial_speed_kmh` và `ego_vehicle.target_speed_kmh`: `pedestrian_crossing` (S25, 10–60 km/h bước 5, CPNA),
`stopped_vehicle` (S42, 10–80 km/h bước 5, CCRs), `crossing_vehicle` (S52, 20–60 km/h bước 10, CCCscp). Bấm
"Use full range" ở tab Variants là ra đúng lưới tốc độ của Euro NCAP. S25 đổi trigger sang `time_to_collision`
(`trigger_ttc_s` 1–3.5 s) để xung đột giữ nguyên ở mọi tốc độ, giống cách Euro NCAP canh người đi bộ.
