-- =====================================================================
-- AutoScenarioX - Sửa content_hash sai của dữ liệu seed (chỉ môi trường DEV)
--
-- VẤN ĐỀ: 100_seed_dev_data.sql tính content_hash bằng SQL trên TOÀN BỘ tài liệu
-- và không điền default. Quy tắc của dự án (CLAUDE.md) là SHA-256 của JSON chuẩn hoá,
-- CHỈ gồm 9 khối: map, route, weather, ego_vehicle, traffic, actors, events,
-- parameters, execution - KHÔNG gồm metadata và source.
--
-- Hệ quả: version SCN-00001 v1 mang hash mà backend không bao giờ tính ra lại,
-- nên ràng buộc uq_versions_hash (chặn publish hai version trùng nội dung)
-- không còn tác dụng với hàng đó.
--
-- Hash đúng đã được kiểm chứng 2026-09-22 bằng CẢ HAI bản validator:
--   - C#     : CanonicalJsonHasher (qua POST /api/v1/scenarios/{id}/draft/validate)
--   - Python : tools/validate_scenario.py -> compute_content_hash()
-- Hai bên cho cùng một giá trị sha256:9cdc2cdf...
--
-- File 100 đã được sửa kèm theo, nên reset lại từ đầu sẽ ra hash đúng ngay và
-- KHÔNG cần chạy file này nữa. File này chỉ dành cho DB đã seed bằng bản cũ.
-- Chạy lại nhiều lần không lỗi.
-- =====================================================================

DO $$
DECLARE
  hash_cu  text := 'sha256:1c9030bbf518c6adbd68cc9abf95c09731f35358dbe197f7dd199d6f864c7676';
  hash_moi text := 'sha256:9cdc2cdf6e31eb2f7b433c0e5fb0db94f86c3e2b9cf92338c914f586a96c7881';
  so_dong  integer;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM asx.scenario_versions WHERE content_hash = hash_cu) THEN
    RAISE NOTICE 'Không có hàng nào mang hash cũ - bỏ qua.';
    RETURN;
  END IF;

  -- scenario_versions là bất biến: trigger chặn mọi thay đổi content_hash.
  -- Tắt tạm để sửa dữ liệu seed sai, rồi bật lại ngay.
  ALTER TABLE asx.scenario_versions DISABLE TRIGGER trg_versions_immutable;

  UPDATE asx.validation_reports SET content_hash = hash_moi WHERE content_hash = hash_cu;
  GET DIAGNOSTICS so_dong = ROW_COUNT;
  RAISE NOTICE 'validation_reports: đã sửa % dòng', so_dong;

  UPDATE asx.scenario_versions SET content_hash = hash_moi WHERE content_hash = hash_cu;
  GET DIAGNOSTICS so_dong = ROW_COUNT;
  RAISE NOTICE 'scenario_versions: đã sửa % dòng', so_dong;

  ALTER TABLE asx.scenario_versions ENABLE TRIGGER trg_versions_immutable;
END $$;

-- Kiểm tra: report và version phải cùng hash mới, và trigger phải đang bật.
SELECT v.version_number,
       v.content_hash                                   AS hash_version,
       r.content_hash                                   AS hash_report,
       (v.content_hash = r.content_hash)                AS khop_nhau,
       (SELECT tgenabled FROM pg_trigger
        WHERE tgname = 'trg_versions_immutable')        AS trigger_status  -- 'O' = đang bật
FROM asx.scenario_versions v
JOIN asx.validation_reports r ON r.id = v.validation_report_id
ORDER BY v.version_number;

