-- evaluations và run_telemetry phải trỏ simulation_runs.
-- Một số database dev còn khóa ngoại trỏ asx.test_runs (bảng cũ), nên worker nộp telemetry bị 422.

ALTER TABLE asx.evaluations DROP CONSTRAINT IF EXISTS evaluations_run_id_fkey;
ALTER TABLE asx.run_telemetry DROP CONSTRAINT IF EXISTS run_telemetry_run_id_fkey;

DELETE FROM asx.violations v
USING asx.evaluations e
WHERE v.evaluation_id = e.id
  AND NOT EXISTS (SELECT 1 FROM asx.simulation_runs s WHERE s.id = e.run_id);

DELETE FROM asx.evaluations e
WHERE NOT EXISTS (SELECT 1 FROM asx.simulation_runs s WHERE s.id = e.run_id);

DELETE FROM asx.run_telemetry r
WHERE NOT EXISTS (SELECT 1 FROM asx.simulation_runs s WHERE s.id = r.run_id);

ALTER TABLE asx.evaluations
  ADD CONSTRAINT evaluations_run_id_fkey
  FOREIGN KEY (run_id) REFERENCES asx.simulation_runs(id) ON DELETE CASCADE;

ALTER TABLE asx.run_telemetry
  ADD CONSTRAINT run_telemetry_run_id_fkey
  FOREIGN KEY (run_id) REFERENCES asx.simulation_runs(id) ON DELETE CASCADE;
