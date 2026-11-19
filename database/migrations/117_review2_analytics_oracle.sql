-- Review 2 administration and reproducible similarity output. Non-destructive.
CREATE TABLE IF NOT EXISTS asx.system_settings (
    id integer PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    active_oracle_config_id uuid REFERENCES asx.oracle_configs(id),
    similarity_warning_percent numeric NOT NULL DEFAULT 85 CHECK (similarity_warning_percent BETWEEN 0 AND 100),
    similarity_block_percent numeric NOT NULL DEFAULT 98 CHECK (similarity_block_percent BETWEEN similarity_warning_percent AND 100),
    retention_days integer NOT NULL DEFAULT 90 CHECK (retention_days BETWEEN 1 AND 3650),
    retention_enabled boolean NOT NULL DEFAULT false,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid REFERENCES asx.app_users(id)
);
INSERT INTO asx.system_settings(id, active_oracle_config_id)
SELECT 1, id FROM asx.oracle_configs ORDER BY version DESC LIMIT 1 ON CONFLICT(id) DO NOTHING;

CREATE OR REPLACE FUNCTION asx.latest_oracle_config_id()
RETURNS uuid LANGUAGE sql STABLE AS $$
    SELECT COALESCE((SELECT active_oracle_config_id FROM asx.system_settings WHERE id=1),
                    (SELECT id FROM asx.oracle_configs ORDER BY version DESC LIMIT 1))
$$;

CREATE TABLE IF NOT EXISTS asx.similarity_analyses (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    method text NOT NULL CHECK (method IN ('gower','dtw')),
    result_json jsonb NOT NULL,
    created_by uuid REFERENCES asx.app_users(id),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_similarity_analyses_created ON asx.similarity_analyses(created_at DESC);

DO $$ DECLARE constraint_name text;
BEGIN
    FOR constraint_name IN SELECT conname FROM pg_constraint
      WHERE conrelid='asx.audit_logs'::regclass AND contype='c'
        AND (pg_get_constraintdef(oid) LIKE '%entity_type%' OR pg_get_constraintdef(oid) LIKE '%action%')
    LOOP EXECUTE format('ALTER TABLE asx.audit_logs DROP CONSTRAINT %I',constraint_name); END LOOP;
END $$;
ALTER TABLE asx.audit_logs ADD CONSTRAINT ck_audit_logs_entity_type CHECK
    (entity_type IN ('SCENARIO','DRAFT','VERSION','IMPORT','VARIANT_BATCH','MAP_METADATA','USER','ORACLE_CONFIG','SYSTEM_SETTINGS','SIMILARITY'));
ALTER TABLE asx.audit_logs ADD CONSTRAINT ck_audit_logs_action CHECK
    (action IN ('CREATE','UPDATE_DRAFT','DISCARD_DRAFT','VALIDATE','PUBLISH','CLONE','IMPORT','EXPORT',
                'GENERATE_VARIANTS','DEPRECATE','ARCHIVE','RESTORE','TAG','UNTAG','UPDATE_PROFILE','ADMIN_UPDATE_USER',
                'CREATE_ORACLE_CONFIG','UPDATE_SETTINGS','COMPARE_SCENARIOS'));

-- Activating a configuration only changes the default for NEW campaigns.
-- Immutable oracle_configs/evaluations and existing campaign.oracle_config_id stay untouched.
-- Retention is persisted as a policy only: referenced artifacts are never deleted by this migration.
