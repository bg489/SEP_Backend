-- =====================================================================
-- AutoScenarioX - Ghi audit khi sửa hồ sơ scenario (UPDATE_PROFILE)
-- Số 113 vì 106 đã dùng cho 106_flow2_campaigns_minimal.sql.
--
-- PATCH /api/v1/scenarios/{id} (tên, mô tả, category) trước đây KHÔNG ghi audit vì CHECK của
-- asx.audit_logs.action (khai báo inline trong 001, Postgres tự đặt tên audit_logs_action_check)
-- không có hành động nào cho thao tác này, nên timeline (tab Activity) thiếu sự kiện sửa hồ sơ.
--
-- Thêm 'UPDATE_PROFILE'. details chỉ lưu các trường thật sự đổi:
--   {"changes": {"name": {"from": "...", "to": "..."}, "category": {...}, "description": {...}}}
--
-- File này chỉ NỚI ràng buộc (thêm một giá trị) nên backend cũ vẫn chạy bình thường.
-- PHẢI chạy TRƯỚC khi chạy backend mới, nếu không PATCH hồ sơ sẽ lỗi 422 (check_violation).
-- Ràng buộc cũ được tìm theo định nghĩa thay vì đoán tên. Chạy lại nhiều lần không lỗi.
-- =====================================================================

DO $$
DECLARE
  existing text;
BEGIN
  FOR existing IN
    SELECT conname FROM pg_constraint
    WHERE conrelid = 'asx.audit_logs'::regclass
      AND contype = 'c'
      AND pg_get_constraintdef(oid) ~ '\maction = ANY'
  LOOP
    EXECUTE format('ALTER TABLE asx.audit_logs DROP CONSTRAINT %I', existing);
  END LOOP;
END $$;

ALTER TABLE asx.audit_logs
  ADD CONSTRAINT ck_audit_logs_action CHECK (action IN
    ('CREATE','UPDATE_DRAFT','DISCARD_DRAFT','VALIDATE','PUBLISH','CLONE','IMPORT',
     'EXPORT','GENERATE_VARIANTS','DEPRECATE','ARCHIVE','RESTORE','TAG','UNTAG',
     'UPDATE_PROFILE'));
