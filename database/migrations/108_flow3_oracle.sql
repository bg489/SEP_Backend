-- =====================================================================
-- AutoScenarioX - Flow 3: Automated Test Oracle
-- Migration 108 - PostgreSQL 15+ (Supabase)
-- Chạy sau 106/107 (campaigns + simulation_runs).
--
-- Flow 3 dựng TRÊN bảng của Flow 2 (asx.campaigns, asx.simulation_runs), không tạo campaign/run riêng.
-- Nguyên tắc (ADR 0001):
--  * oracle_configs BẤT BIẾN: đổi ngưỡng thì tạo version mới; campaign ghim một version.
--  * evaluations BẤT BIẾN: chấm lại thì thêm Evaluation mới, không ghi đè.
--  * simulation_runs.verdict chỉ là bản sao của Evaluation theo Oracle Config campaign ghim.
-- Chạy một lần trong Supabase SQL editor; sau đó health check: 14 + 4 = 18 bảng.
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. oracle_configs - bộ ngưỡng bất biến
-- ---------------------------------------------------------------------
CREATE TABLE asx.oracle_configs (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version                 integer NOT NULL UNIQUE CHECK (version >= 1),
    name                    text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 120),
    description             text CHECK (char_length(description) <= 1000),
    -- Ngưỡng cho từng Check (xem CONTEXT.md)
    speeding_tolerance_kmh  numeric NOT NULL DEFAULT 5   CHECK (speeding_tolerance_kmh >= 0),
    speeding_min_duration_s numeric NOT NULL DEFAULT 2   CHECK (speeding_min_duration_s > 0),
    freezing_max_still_s    numeric NOT NULL DEFAULT 10  CHECK (freezing_max_still_s > 0),
    ttc_threshold_s         numeric NOT NULL DEFAULT 2   CHECK (ttc_threshold_s > 0),
    route_goal_tolerance_m  numeric NOT NULL DEFAULT 5   CHECK (route_goal_tolerance_m > 0),
    created_by              uuid REFERENCES asx.app_users(id),
    created_at              timestamptz NOT NULL DEFAULT now()
);

INSERT INTO asx.oracle_configs (version, name, description)
VALUES (1, 'Default Oracle Config v1', 'Default thresholds for the Oracle.');

CREATE OR REPLACE FUNCTION asx.guard_oracle_config_immutable()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'oracle_configs are immutable (id %)', OLD.id
        USING ERRCODE = 'integrity_constraint_violation';
END $$;

CREATE TRIGGER trg_oracle_configs_immutable
    BEFORE UPDATE OR DELETE ON asx.oracle_configs
    FOR EACH ROW EXECUTE FUNCTION asx.guard_oracle_config_immutable();

-- Version mới nhất: mặc định cho campaign mới (Flow 2 không cần biết cột này)
CREATE OR REPLACE FUNCTION asx.latest_oracle_config_id()
RETURNS uuid LANGUAGE sql STABLE AS $$
    SELECT id FROM asx.oracle_configs ORDER BY version DESC LIMIT 1
$$;

-- ---------------------------------------------------------------------
-- 2. campaigns (Flow 2): ghim Oracle Config. Campaign cũ nhận v1.
-- ---------------------------------------------------------------------
ALTER TABLE asx.campaigns
    ADD COLUMN oracle_config_id uuid REFERENCES asx.oracle_configs(id)
    DEFAULT asx.latest_oracle_config_id();
UPDATE asx.campaigns SET oracle_config_id = asx.latest_oracle_config_id() WHERE oracle_config_id IS NULL;
ALTER TABLE asx.campaigns ALTER COLUMN oracle_config_id SET NOT NULL;

-- ---------------------------------------------------------------------
-- 3. run_telemetry - Telemetry đã nhận của một simulation run (file .jsonl.gz trên server)
-- ---------------------------------------------------------------------
CREATE TABLE asx.run_telemetry (
    run_id          uuid PRIMARY KEY REFERENCES asx.simulation_runs(id) ON DELETE CASCADE,
    telemetry_path  text NOT NULL,
    -- Run Outcome do runner ghi trong bản ghi end; NULL khi file không đọc được
    outcome         text CHECK (outcome IN ('COMPLETED','TIMED_OUT','STOPPED_ON_VIOLATION','INFRA_FAILED')),
    duration_s      numeric,
    received_at     timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------
-- 4. evaluations - một lần chấm một run với một Oracle Config
-- ---------------------------------------------------------------------
CREATE TABLE asx.evaluations (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id              uuid NOT NULL REFERENCES asx.simulation_runs(id) ON DELETE CASCADE,
    oracle_config_id    uuid NOT NULL REFERENCES asx.oracle_configs(id),
    verdict             text NOT NULL CHECK (verdict IN ('PASS','FAIL','ERROR')),
    error_reason        text,
    evaluated_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_evaluations_run_config UNIQUE (run_id, oracle_config_id),
    CONSTRAINT ck_evaluations_error CHECK ((verdict = 'ERROR') = (error_reason IS NOT NULL))
);
CREATE INDEX ix_evaluations_run ON asx.evaluations (run_id);

-- Không cho sửa; vẫn cho xoá theo cascade khi Flow 2 xoá campaign/run.
CREATE OR REPLACE FUNCTION asx.guard_evaluation_immutable()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'evaluations are immutable (id %)', OLD.id
        USING ERRCODE = 'integrity_constraint_violation';
END $$;

CREATE TRIGGER trg_evaluations_immutable
    BEFORE UPDATE ON asx.evaluations
    FOR EACH ROW EXECUTE FUNCTION asx.guard_evaluation_immutable();

-- ---------------------------------------------------------------------
-- 5. violations - từng vi phạm trong một Evaluation
-- ---------------------------------------------------------------------
CREATE TABLE asx.violations (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    evaluation_id   uuid NOT NULL REFERENCES asx.evaluations(id) ON DELETE CASCADE,
    check_name      text NOT NULL CHECK (check_name IN (
                    'collision','speeding','freezing','ttc','lane_invasion','red_light','route_incomplete')),
    severity        text NOT NULL CHECK (severity IN ('FAILING','WARNING')),
    t               numeric NOT NULL,
    duration_s      numeric,
    details         jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(details) = 'object'),
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_violations_evaluation ON asx.violations (evaluation_id);

-- RLS giống mọi bảng asx: bật, không policy, chỉ backend truy cập.
ALTER TABLE asx.oracle_configs ENABLE ROW LEVEL SECURITY;
ALTER TABLE asx.run_telemetry ENABLE ROW LEVEL SECURITY;
ALTER TABLE asx.evaluations ENABLE ROW LEVEL SECURITY;
ALTER TABLE asx.violations ENABLE ROW LEVEL SECURITY;
