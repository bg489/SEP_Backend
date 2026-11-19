-- Additive protections for Auth/Admin. Existing app_users remains the source of application roles.
-- Supabase auth.users stores credentials; app_users never stores passwords.
CREATE UNIQUE INDEX IF NOT EXISTS uq_app_users_email_normalized ON asx.app_users (lower(email));

CREATE OR REPLACE FUNCTION asx.protect_last_active_admin()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.role = 'ADMIN' AND OLD.is_active
       AND (TG_OP = 'DELETE' OR NEW.role <> 'ADMIN' OR NOT NEW.is_active) THEN
        -- Serialize administrative removals, including concurrent requests from different admins.
        PERFORM pg_advisory_xact_lock(270011503);
        IF NOT EXISTS (SELECT 1 FROM asx.app_users WHERE role = 'ADMIN' AND is_active AND id <> OLD.id) THEN
            RAISE EXCEPTION 'Keep at least one active administrator' USING ERRCODE = '23514';
        END IF;
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_app_users_last_admin ON asx.app_users;
CREATE TRIGGER trg_app_users_last_admin BEFORE UPDATE OR DELETE ON asx.app_users
FOR EACH ROW EXECUTE FUNCTION asx.protect_last_active_admin();

-- Keep audit history for account administration using the existing audit table.
DO $$
DECLARE constraint_name text;
BEGIN
    FOR constraint_name IN
        SELECT conname FROM pg_constraint
        WHERE conrelid = 'asx.audit_logs'::regclass AND contype = 'c'
          AND (pg_get_constraintdef(oid) LIKE '%entity_type%' OR pg_get_constraintdef(oid) LIKE '%action%')
    LOOP
        EXECUTE format('ALTER TABLE asx.audit_logs DROP CONSTRAINT %I', constraint_name);
    END LOOP;
END $$;
ALTER TABLE asx.audit_logs ADD CONSTRAINT ck_audit_logs_entity_type CHECK
    (entity_type IN ('SCENARIO','DRAFT','VERSION','IMPORT','VARIANT_BATCH','MAP_METADATA','USER'));
ALTER TABLE asx.audit_logs ADD CONSTRAINT ck_audit_logs_action CHECK
    (action IN ('CREATE','UPDATE_DRAFT','DISCARD_DRAFT','VALIDATE','PUBLISH','CLONE','IMPORT','EXPORT',
                'GENERATE_VARIANTS','DEPRECATE','ARCHIVE','RESTORE','TAG','UNTAG','UPDATE_PROFILE','ADMIN_UPDATE_USER'));
