-- =====================================================================
-- AutoScenarioX - Flow 2 minimal: Test campaigns (chọn READY versions)
-- Chạy trên Supabase SQL Editor (schema asx đã có từ 001).
-- An toàn chạy lại: IF NOT EXISTS / ON CONFLICT.
-- =====================================================================

CREATE TABLE IF NOT EXISTS asx.campaigns (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  seq_no          bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
  code            text GENERATED ALWAYS AS ('CMP-' || lpad(seq_no::text, 5, '0')) STORED,
  name            text NOT NULL CHECK (char_length(name) BETWEEN 3 AND 120),
  description     text CHECK (description IS NULL OR char_length(description) <= 2000),
  strategy        text NOT NULL DEFAULT 'proposed_failure_history',
  status          text NOT NULL DEFAULT 'Queued'
                  CHECK (status IN ('Draft','Queued','Running','Completed','Needs review')),
  progress        integer NOT NULL DEFAULT 0 CHECK (progress BETWEEN 0 AND 100),
  pass_rate       numeric(5,2) NOT NULL DEFAULT 0,
  failed_runs     integer NOT NULL DEFAULT 0,
  estimated_duration text NOT NULL DEFAULT '—',
  created_by      uuid NOT NULL REFERENCES asx.app_users(id),
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS asx.campaign_versions (
  campaign_id          uuid NOT NULL REFERENCES asx.campaigns(id) ON DELETE CASCADE,
  scenario_version_id  uuid NOT NULL REFERENCES asx.scenario_versions(id),
  sort_order           integer NOT NULL DEFAULT 0,
  PRIMARY KEY (campaign_id, scenario_version_id)
);

CREATE INDEX IF NOT EXISTS ix_campaign_versions_version
  ON asx.campaign_versions (scenario_version_id);

CREATE INDEX IF NOT EXISTS ix_campaigns_created_at
  ON asx.campaigns (created_at DESC);

DROP TRIGGER IF EXISTS trg_campaigns_updated_at ON asx.campaigns;
CREATE TRIGGER trg_campaigns_updated_at
  BEFORE UPDATE ON asx.campaigns
  FOR EACH ROW EXECUTE FUNCTION asx.set_updated_at();
