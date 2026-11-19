-- Flow 3: infra retry counter + optional video path on run_telemetry
-- Chạy sau 108_flow3_oracle.sql

ALTER TABLE asx.simulation_runs
  ADD COLUMN IF NOT EXISTS infra_retry_count integer NOT NULL DEFAULT 0
    CHECK (infra_retry_count >= 0);

ALTER TABLE asx.run_telemetry
  ADD COLUMN IF NOT EXISTS video_path text;

COMMENT ON COLUMN asx.simulation_runs.infra_retry_count IS
  'Số lần đã re-queue vì INFRA_FAILED; worker dừng retry khi đạt ngưỡng cấu hình.';
COMMENT ON COLUMN asx.run_telemetry.video_path IS
  'Đường dẫn tương đối mp4 trong Telemetry:Directory (null nếu chưa ghi video).';
