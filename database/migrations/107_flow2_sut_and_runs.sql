-- =====================================================================
-- Flow 2: SUT config on campaigns + simulation_runs (chuẩn bị Flow 3)
-- An toàn chạy lại: IF NOT EXISTS / ADD COLUMN IF NOT EXISTS.
-- =====================================================================

-- SUT thuộc campaign (không thuộc scenario Flow 1)
ALTER TABLE asx.campaigns
  ADD COLUMN IF NOT EXISTS sut_type text NOT NULL DEFAULT 'behavior_agent'
    CHECK (sut_type IN ('behavior_agent', 'autopilot')),
  ADD COLUMN IF NOT EXISTS sut_behavior text NOT NULL DEFAULT 'normal'
    CHECK (sut_behavior IN ('cautious', 'normal', 'aggressive')),
  ADD COLUMN IF NOT EXISTS sut_carla_version text NOT NULL DEFAULT '0.9.16',
  ADD COLUMN IF NOT EXISTS started_at timestamptz;

CREATE TABLE IF NOT EXISTS asx.simulation_runs (
  id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  campaign_id          uuid NOT NULL REFERENCES asx.campaigns(id) ON DELETE CASCADE,
  scenario_version_id  uuid NOT NULL REFERENCES asx.scenario_versions(id),
  sort_order           integer NOT NULL DEFAULT 0,
  status               text NOT NULL DEFAULT 'Queued'
                       CHECK (status IN ('Queued','Claimed','Running','Completed','Failed','TimedOut','Error')),
  verdict              text CHECK (verdict IS NULL OR verdict IN ('PASS','FAIL','ERROR')),
  content_hash         text,
  seed                 integer,
  started_at           timestamptz,
  finished_at          timestamptz,
  error_message        text,
  created_at           timestamptz NOT NULL DEFAULT now(),
  updated_at           timestamptz NOT NULL DEFAULT now(),
  UNIQUE (campaign_id, scenario_version_id)
);

CREATE INDEX IF NOT EXISTS ix_simulation_runs_campaign
  ON asx.simulation_runs (campaign_id, sort_order);

CREATE INDEX IF NOT EXISTS ix_simulation_runs_status
  ON asx.simulation_runs (status, created_at);

DROP TRIGGER IF EXISTS trg_simulation_runs_updated_at ON asx.simulation_runs;
CREATE TRIGGER trg_simulation_runs_updated_at
  BEFORE UPDATE ON asx.simulation_runs
  FOR EACH ROW EXECUTE FUNCTION asx.set_updated_at();
