-- Flow 4: phiên phân tích lỗi sau Oracle. Một campaign một phiên.
-- Chạy sau 110_point_oracle_fks_at_simulation_runs.sql

CREATE TABLE IF NOT EXISTS asx.failure_analyses (
  id uuid PRIMARY KEY,
  campaign_id uuid NOT NULL UNIQUE REFERENCES asx.campaigns(id) ON DELETE CASCADE,
  status text NOT NULL CHECK (status IN (
    'grouped', 'verified', 'similar_found', 'awaiting_flow3',
    'explainable', 'report_draft', 'needs_more', 'evaluated')),
  summary text NOT NULL DEFAULT '',
  review_note text,
  effectiveness jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS asx.failure_faults (
  id uuid PRIMARY KEY,
  analysis_id uuid NOT NULL REFERENCES asx.failure_analyses(id) ON DELETE CASCADE,
  root_scenario_id uuid NOT NULL,
  root_scenario_code text NOT NULL,
  root_scenario_name text NOT NULL,
  check_name text NOT NULL,
  status text NOT NULL CHECK (status IN (
    'grouped', 'verified', 'similar_found', 'returned_to_flow3', 'explainable', 'in_report')),
  first_run_id uuid NOT NULL,
  first_t numeric NOT NULL,
  explanation text,
  report_text text
);

CREATE TABLE IF NOT EXISTS asx.failure_fault_runs (
  fault_id uuid NOT NULL REFERENCES asx.failure_faults(id) ON DELETE CASCADE,
  run_id uuid NOT NULL REFERENCES asx.simulation_runs(id) ON DELETE CASCADE,
  is_primary boolean NOT NULL DEFAULT false,
  PRIMARY KEY (fault_id, run_id)
);

COMMENT ON TABLE asx.failure_analyses IS
  'Flow 4. APFD chỉ được ghi khi status = evaluated, sau khi reviewer chốt report.';
