-- Preserve execution history, atomic queue claims and persisted worker capacity.
BEGIN;
CREATE TABLE IF NOT EXISTS asx.simulation_workers (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 120),
    host text NOT NULL,
    rpc_port integer NOT NULL DEFAULT 2000 CHECK (rpc_port BETWEEN 1 AND 65535),
    capacity integer NOT NULL DEFAULT 1 CHECK (capacity BETWEEN 1 AND 16),
    enabled boolean NOT NULL DEFAULT true,
    quality text NOT NULL DEFAULT 'Low' CHECK (quality = 'Low'),
    carla_version text NOT NULL DEFAULT '0.9.16',
    maps_json jsonb NOT NULL DEFAULT '[]',
    owner_user_id uuid NOT NULL REFERENCES asx.app_users(id),
    last_heartbeat_at timestamptz,
    last_error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(owner_user_id, name)
);
ALTER TABLE asx.simulation_runs
    ADD COLUMN IF NOT EXISTS attempt integer NOT NULL DEFAULT 1 CHECK (attempt >= 1),
    ADD COLUMN IF NOT EXISTS previous_run_id uuid REFERENCES asx.simulation_runs(id),
    ADD COLUMN IF NOT EXISTS is_current boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS worker_id uuid REFERENCES asx.simulation_workers(id),
    ADD COLUMN IF NOT EXISTS telemetry_sha256 text,
    ADD COLUMN IF NOT EXISTS progress_json jsonb,
    ADD COLUMN IF NOT EXISTS progress_at timestamptz;
ALTER TABLE asx.run_telemetry ADD COLUMN IF NOT EXISTS video_sha256 text;

-- A scenario may have many historical attempts but only one current attempt in a campaign.
ALTER TABLE asx.simulation_runs DROP CONSTRAINT IF EXISTS simulation_runs_campaign_id_scenario_version_id_key;
DROP INDEX IF EXISTS asx.ix_simulation_runs_campaign_id_scenario_version_id;
CREATE UNIQUE INDEX IF NOT EXISTS uq_simulation_run_attempt ON asx.simulation_runs(campaign_id, scenario_version_id, attempt);
CREATE UNIQUE INDEX IF NOT EXISTS uq_simulation_run_current ON asx.simulation_runs(campaign_id, scenario_version_id) WHERE is_current;

CREATE OR REPLACE FUNCTION asx.claim_next_run(p_campaign_id uuid DEFAULT NULL, p_worker_id uuid DEFAULT NULL)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE claimed uuid; slots integer; active_count integer;
BEGIN
    IF p_worker_id IS NOT NULL THEN
        -- Serialize capacity decisions for this worker while other workers can claim independently.
        SELECT capacity INTO slots FROM asx.simulation_workers
        WHERE id=p_worker_id AND enabled AND last_error IS NULL
          AND last_heartbeat_at >= now() - interval '90 seconds' FOR UPDATE;
        IF slots IS NULL THEN RETURN NULL; END IF;
        SELECT count(*) INTO active_count FROM asx.simulation_runs
        WHERE worker_id=p_worker_id AND is_current AND status IN ('Claimed','Running');
        IF active_count >= slots THEN RETURN NULL; END IF;
    END IF;
    WITH candidate AS (
        SELECT r.id FROM asx.simulation_runs r
        WHERE r.status='Queued' AND r.is_current
          AND (p_campaign_id IS NULL OR r.campaign_id=p_campaign_id)
          AND (p_worker_id IS NULL OR EXISTS (
            SELECT 1 FROM asx.scenario_versions v JOIN asx.simulation_workers w ON w.id=p_worker_id
            WHERE v.id=r.scenario_version_id AND w.maps_json ? v.map_name))
        ORDER BY r.created_at, r.campaign_id, r.sort_order
        FOR UPDATE OF r SKIP LOCKED LIMIT 1
    )
    UPDATE asx.simulation_runs r SET status='Claimed', worker_id=p_worker_id,
        started_at=COALESCE(r.started_at,now()), updated_at=now()
    FROM candidate c WHERE r.id=c.id RETURNING r.id INTO claimed;
    RETURN claimed;
END $$;

CREATE OR REPLACE FUNCTION asx.recover_lost_workers()
RETURNS integer LANGUAGE plpgsql AS $$
DECLARE stale record; recovered integer := 0; replacement uuid;
BEGIN
    FOR stale IN
        SELECT r.* FROM asx.simulation_runs r JOIN asx.simulation_workers w ON w.id=r.worker_id
        WHERE r.is_current AND r.status IN ('Claimed','Running')
          AND (NOT w.enabled OR w.last_heartbeat_at < now() - interval '90 seconds')
        FOR UPDATE OF r SKIP LOCKED
    LOOP
        UPDATE asx.simulation_runs SET is_current=(stale.infra_retry_count>=2),status='Error',verdict='ERROR',
            error_message='Worker heartbeat expired; evidence retained.',finished_at=now() WHERE id=stale.id;
        IF stale.infra_retry_count >= 2 THEN CONTINUE; END IF;
        replacement := gen_random_uuid();
        INSERT INTO asx.simulation_runs(id,campaign_id,scenario_version_id,sort_order,status,content_hash,seed,
            attempt,previous_run_id,is_current,infra_retry_count)
        VALUES(replacement,stale.campaign_id,stale.scenario_version_id,stale.sort_order,'Queued',stale.content_hash,
            stale.seed,stale.attempt+1,stale.id,true,stale.infra_retry_count+1);
        UPDATE asx.campaigns SET status='Running' WHERE id=stale.campaign_id;
        recovered := recovered + 1;
    END LOOP;
    RETURN recovered;
END $$;
COMMIT;
