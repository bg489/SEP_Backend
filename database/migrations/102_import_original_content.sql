-- =====================================================================
-- AutoScenarioX - Lưu nguyên văn file gốc của mỗi lần import vào DB
--
-- Thiết kế ban đầu (001) định để file gốc trong Supabase Storage, bucket
-- "scenario-imports", và storage_path là NOT NULL. Dự án chưa tích hợp Storage,
-- mà truy xuất nguồn thì bắt buộc phải giữ được nguyên văn file người dùng nạp lên.
--
-- Quyết định 2026-09-22: lưu thẳng nội dung vào DB.
--   * File scenario chỉ cỡ chục KB (Scenario:MaxContentSizeKb = 256 chặn ở API).
--   * Không cần bucket, không cần service_role key (khoá bỏ qua RLS).
--   * Vẫn giữ storage_path cho tương lai: khi nào chuyển sang Storage thì ghi
--     đường dẫn vào đó, hai cột cùng tồn tại, không phá dữ liệu cũ.
--
-- Chạy lại nhiều lần không lỗi.
-- =====================================================================

ALTER TABLE asx.import_records
  ADD COLUMN IF NOT EXISTS original_content text;

ALTER TABLE asx.import_records
  ALTER COLUMN storage_path DROP NOT NULL;

-- Phải giữ được file gốc bằng ÍT NHẤT một trong hai cách.
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'ck_import_original_kept'
      AND conrelid = 'asx.import_records'::regclass
  ) THEN
    ALTER TABLE asx.import_records
      ADD CONSTRAINT ck_import_original_kept
      CHECK (original_content IS NOT NULL OR storage_path IS NOT NULL);
  END IF;
END $$;

COMMENT ON COLUMN asx.import_records.original_content IS
  'Nguyên văn file người dùng nạp lên (JSON/YAML). Dùng để truy xuất nguồn và import lại.';
COMMENT ON COLUMN asx.import_records.storage_path IS
  'Đường dẫn trong Supabase Storage nếu sau này chuyển sang lưu file ngoài. NULL khi dùng original_content.';

-- Kiểm tra
SELECT column_name, is_nullable, data_type
FROM information_schema.columns
WHERE table_schema = 'asx' AND table_name = 'import_records'
  AND column_name IN ('storage_path', 'original_content')
ORDER BY column_name;
