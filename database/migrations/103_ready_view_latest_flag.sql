-- =====================================================================
-- AutoScenarioX - Nhiều version READY song song, thêm cờ is_latest_ready
--
-- Quyết định 2026-09-26: publish KHÔNG còn tự chuyển version trước sang
-- DEPRECATED. Một scenario có thể có nhiều version READY cùng lúc để Flow 2
-- chạy lại đúng cấu hình cũ (hồi quy, so sánh v1/v2, APFD).
--   * READY/DEPRECATED chỉ trả lời "còn được phép dùng không".
--   * "Mới nhất" là con trỏ scenarios.latest_version_id, không phải trạng thái.
--   * DEPRECATED chỉ do người dùng chủ động ngừng dùng.
--
-- View bàn giao thêm cột is_latest_ready (ở CUỐI để CREATE OR REPLACE hợp lệ):
-- true cho version READY có số lớn nhất của mỗi scenario -> Flow 2 dùng làm
-- lựa chọn mặc định; muốn chạy bản cũ thì chọn scenario_version_id cụ thể.
-- Không đổi bảng, ràng buộc hay trigger. Chạy lại nhiều lần không lỗi.
-- =====================================================================

CREATE OR REPLACE VIEW asx.v_ready_scenario_versions AS
SELECT v.id AS scenario_version_id, s.id AS scenario_id, s.code, s.name, s.category,
       v.version_number, v.map_name, v.weather_preset, v.event_types, v.event_count,
       v.actor_count, v.npc_vehicle_count, v.pedestrian_count, v.speed_limit_kmh, v.ego_blueprint,
       v.content, v.content_hash, v.published_at,
       (v.version_number = max(v.version_number) OVER (PARTITION BY v.scenario_id)) AS is_latest_ready
FROM asx.scenario_versions v
JOIN asx.scenarios s ON s.id = v.scenario_id
WHERE v.status = 'READY' AND s.status = 'ACTIVE';

