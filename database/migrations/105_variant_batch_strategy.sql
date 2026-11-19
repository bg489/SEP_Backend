-- =====================================================================
-- AutoScenarioX - Chiến lược sinh biến thể (cartesian | pairwise)
--
-- Quyết định 2026-10-09: ngoài tích Descartes của parameter_grid, cho phép sinh
-- bộ pairwise (phủ mọi cặp giá trị của hai tham số bất kỳ, thuật toán IPOG tất
-- định) để giảm số scenario khi grid lớn.
--   * parameter_grid giữ nguyên ý nghĩa: các giá trị ứng viên của từng tham số.
--   * requested_count/generated_count = số tổ hợp thực sự sinh ra theo strategy.
--   * Batch cũ được gán 'cartesian' qua DEFAULT.
-- Chạy lại nhiều lần không lỗi.
-- =====================================================================

ALTER TABLE asx.variant_batches
  ADD COLUMN IF NOT EXISTS strategy text NOT NULL DEFAULT 'cartesian';

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint
                 WHERE conname = 'ck_variant_batches_strategy'
                   AND conrelid = 'asx.variant_batches'::regclass) THEN
    ALTER TABLE asx.variant_batches
      ADD CONSTRAINT ck_variant_batches_strategy CHECK (strategy IN ('cartesian', 'pairwise'));
  END IF;
END $$;
