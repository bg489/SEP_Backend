-- =====================================================================
-- AutoScenarioX - Flow 1: Scenario Management
-- Migration 001 - PostgreSQL 15+ (Supabase)
--
-- Nguyên tắc:
--  * Toàn bộ bảng nằm trong schema riêng "asx", KHÔNG expose qua Supabase
--    Data API. Chỉ backend ASP.NET Core truy cập (qua Npgsql / EF Core).
--  * Enum lưu dạng TEXT + CHECK (dễ map với EF Core, dễ thêm giá trị mới).
--  * scenario_versions là BẤT BIẾN: trigger chặn sửa nội dung và chặn xoá.
--  * content_hash do backend tính (chuẩn hoá JSON ở .NET), DB chỉ lưu và
--    ràng buộc duy nhất.
-- =====================================================================

CREATE SCHEMA IF NOT EXISTS asx;

-- ---------------------------------------------------------------------
-- 0. Hàm tiện ích
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION asx.set_updated_at()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  NEW.updated_at := now();
  RETURN NEW;
END $$;

-- ---------------------------------------------------------------------
-- 1. app_users (TỐI THIỂU - thuộc Flow Admin, chỉ tạo để FK hoạt động)
--    Nếu dùng Supabase Auth: id = auth.users.id (xem ghi chú cuối file).
-- ---------------------------------------------------------------------
CREATE TABLE asx.app_users (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  email         text NOT NULL,
  display_name  text NOT NULL,
  role          text NOT NULL DEFAULT 'SCENARIO_ENGINEER'
                CHECK (role IN ('ADMIN','SCENARIO_ENGINEER','TEST_ENGINEER','TEST_REVIEWER')),
  is_active     boolean NOT NULL DEFAULT true,
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_app_users_email ON asx.app_users (lower(email));

-- ---------------------------------------------------------------------
-- 2. map_metadata - dữ liệu cho validation tầng 3 + dropdown ở UI
--    Nạp từ file JSON do tools/export_map_metadata.py sinh ra.
-- ---------------------------------------------------------------------
CREATE TABLE asx.map_metadata (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  map_name            text NOT NULL CHECK (map_name ~ '^Town[0-9]{2}(HD)?(_Opt)?$'),
  carla_version       text NOT NULL,
  spawn_points        jsonb NOT NULL CHECK (jsonb_typeof(spawn_points) = 'array'),
  spawn_point_count   integer GENERATED ALWAYS AS (jsonb_array_length(spawn_points)) STORED,
  preview_image_path  text,              -- đường dẫn trong Supabase Storage (bucket map-previews)
  is_active           boolean NOT NULL DEFAULT true,
  imported_by         uuid REFERENCES asx.app_users(id),
  imported_at         timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_map_metadata UNIQUE (map_name, carla_version)
);
-- Mỗi map chỉ có 1 bản metadata đang active
CREATE UNIQUE INDEX ux_map_metadata_active ON asx.map_metadata (map_name) WHERE is_active;

-- ---------------------------------------------------------------------
-- 3. variant_batches - một lần "Generate Variants" (parameter sweep)
-- ---------------------------------------------------------------------
CREATE TABLE asx.variant_batches (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  base_version_id  uuid NOT NULL,               -- FK thêm ở cuối (bảng versions chưa tạo)
  parameter_grid   jsonb NOT NULL,              -- vd {"trigger_distance_m":[5,10,20]}
  requested_count  integer NOT NULL CHECK (requested_count > 0),
  generated_count  integer NOT NULL DEFAULT 0 CHECK (generated_count >= 0),
  note             text,
  created_by       uuid NOT NULL REFERENCES asx.app_users(id),
  created_at       timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------
-- 4. scenarios - "hồ sơ" của một scenario (thay đổi được)
-- ---------------------------------------------------------------------
CREATE TABLE asx.scenarios (
  id                     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  seq_no                 bigint GENERATED ALWAYS AS IDENTITY,
  code                   text GENERATED ALWAYS AS ('SCN-' || lpad(seq_no::text, 5, '0')) STORED,
  name                   text NOT NULL CHECK (char_length(name) BETWEEN 3 AND 120),
  description            text CHECK (char_length(description) <= 2000),
  category               text NOT NULL CHECK (category IN
                         ('pedestrian','cut_in','braking','traffic_light','following',
                          'intersection','obstacle','free_driving','other')),
  status                 text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','ARCHIVED')),
  -- nguồn gốc scenario (lần tạo đầu tiên)
  origin_type            text NOT NULL CHECK (origin_type IN ('manual','import','clone','variant')),
  origin_version_id      uuid,                  -- version cha nếu clone/variant (FK ở cuối)
  variant_batch_id       uuid REFERENCES asx.variant_batches(id),
  -- con trỏ tới version mới nhất (denormalize để list nhanh)
  latest_version_id      uuid,                  -- FK ở cuối
  latest_version_number  integer NOT NULL DEFAULT 0 CHECK (latest_version_number >= 0),
  created_by             uuid NOT NULL REFERENCES asx.app_users(id),
  created_at             timestamptz NOT NULL DEFAULT now(),
  updated_at             timestamptz NOT NULL DEFAULT now(),
  archived_by            uuid REFERENCES asx.app_users(id),
  archived_at            timestamptz,
  CONSTRAINT uq_scenarios_code UNIQUE (code),
  CONSTRAINT ck_scenarios_origin_parent CHECK (
    (origin_type IN ('clone','variant')) = (origin_version_id IS NOT NULL)),
  CONSTRAINT ck_scenarios_variant_batch CHECK (
    (origin_type = 'variant') = (variant_batch_id IS NOT NULL)),
  CONSTRAINT ck_scenarios_archived CHECK (
    (status = 'ARCHIVED') = (archived_at IS NOT NULL))
);
CREATE INDEX ix_scenarios_status_category ON asx.scenarios (status, category);
CREATE INDEX ix_scenarios_name_lower ON asx.scenarios (lower(name));
CREATE TRIGGER trg_scenarios_updated_at BEFORE UPDATE ON asx.scenarios
  FOR EACH ROW EXECUTE FUNCTION asx.set_updated_at();

-- ---------------------------------------------------------------------
-- 5. validation_reports - kết quả mỗi lần validate
-- ---------------------------------------------------------------------
CREATE TABLE asx.validation_reports (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  scenario_id        uuid NOT NULL REFERENCES asx.scenarios(id),
  content_hash       text NOT NULL CHECK (content_hash ~ '^sha256:[a-f0-9]{64}$'),
  is_valid           boolean NOT NULL,
  stopped_at         text CHECK (stopped_at IN ('schema','business','carla')),
  errors             jsonb NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(errors) = 'array'),
  warnings           jsonb NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(warnings) = 'array'),
  schema_version     text NOT NULL,
  validator_version  text NOT NULL,
  map_metadata_id    uuid REFERENCES asx.map_metadata(id),   -- metadata đã dùng cho tầng 3
  created_by         uuid NOT NULL REFERENCES asx.app_users(id),
  created_at         timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_validation_consistency CHECK (
    (is_valid AND stopped_at IS NULL AND jsonb_array_length(errors) = 0)
    OR (NOT is_valid AND stopped_at IS NOT NULL AND jsonb_array_length(errors) > 0))
);
CREATE INDEX ix_validation_reports_lookup
  ON asx.validation_reports (scenario_id, content_hash, created_at DESC);

-- ---------------------------------------------------------------------
-- 6. scenario_versions - BẤT BIẾN, chỉ được thêm, không sửa nội dung
-- ---------------------------------------------------------------------
CREATE TABLE asx.scenario_versions (
  id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  scenario_id           uuid NOT NULL REFERENCES asx.scenarios(id) ON DELETE RESTRICT,
  version_number        integer NOT NULL CHECK (version_number >= 1),
  schema_version        text NOT NULL,
  content               jsonb NOT NULL CHECK (jsonb_typeof(content) = 'object'),
  content_hash          text NOT NULL CHECK (content_hash ~ '^sha256:[a-f0-9]{64}$'),
  -- cột sinh tự động từ content: phục vụ lọc/tìm kiếm và feature cho Flow 5
  map_name              text  GENERATED ALWAYS AS (content #>> '{map,name}') STORED,
  weather_preset        text  GENERATED ALWAYS AS (coalesce(content #>> '{weather,preset}', 'ClearNoon')) STORED,
  event_types           jsonb GENERATED ALWAYS AS (jsonb_path_query_array(content, '$.events[*].type')) STORED,
  actor_count           integer GENERATED ALWAYS AS (coalesce(jsonb_array_length(content -> 'actors'), 0)) STORED,
  event_count           integer GENERATED ALWAYS AS (coalesce(jsonb_array_length(content -> 'events'), 0)) STORED,
  -- NULL nếu giá trị là tham chiếu {"$param": ...} (giá trị thật nằm trong parameters)
  npc_vehicle_count     integer GENERATED ALWAYS AS (CASE WHEN jsonb_typeof(content #> '{traffic,npc_vehicles,count}') = 'number'
                                  THEN (content #>> '{traffic,npc_vehicles,count}')::integer END) STORED,
  pedestrian_count      integer GENERATED ALWAYS AS (CASE WHEN jsonb_typeof(content #> '{traffic,pedestrians,count}') = 'number'
                                  THEN (content #>> '{traffic,pedestrians,count}')::integer END) STORED,
  speed_limit_kmh       numeric GENERATED ALWAYS AS (CASE WHEN jsonb_typeof(content #> '{route,speed_limit_kmh}') = 'number'
                                  THEN (content #>> '{route,speed_limit_kmh}')::numeric END) STORED,
  ego_blueprint         text  GENERATED ALWAYS AS (coalesce(content #>> '{ego_vehicle,blueprint}', 'vehicle.tesla.model3')) STORED,
  -- vòng đời
  status                text NOT NULL DEFAULT 'READY' CHECK (status IN ('READY','DEPRECATED')),
  change_note           text CHECK (char_length(change_note) <= 1000),
  -- nguồn gốc của riêng version này
  source_type           text NOT NULL CHECK (source_type IN ('manual','import','clone','variant')),
  parent_version_id     uuid REFERENCES asx.scenario_versions(id),
  import_record_id      uuid,                        -- FK ở cuối
  validation_report_id  uuid NOT NULL REFERENCES asx.validation_reports(id),
  published_by          uuid NOT NULL REFERENCES asx.app_users(id),
  published_at          timestamptz NOT NULL DEFAULT now(),
  deprecated_by         uuid REFERENCES asx.app_users(id),
  deprecated_at         timestamptz,
  CONSTRAINT uq_versions_number UNIQUE (scenario_id, version_number),
  CONSTRAINT uq_versions_hash   UNIQUE (scenario_id, content_hash),   -- không publish 2 version y hệt
  CONSTRAINT ck_versions_deprecated CHECK ((status = 'DEPRECATED') = (deprecated_at IS NOT NULL)),
  CONSTRAINT ck_versions_parent CHECK (
    source_type NOT IN ('clone','variant') OR parent_version_id IS NOT NULL)
);
CREATE INDEX ix_versions_ready      ON asx.scenario_versions (status, map_name);
CREATE INDEX ix_versions_content    ON asx.scenario_versions USING gin (content jsonb_path_ops);
CREATE INDEX ix_versions_event_type ON asx.scenario_versions USING gin (event_types);

-- Trigger bảo vệ tính bất biến
CREATE OR REPLACE FUNCTION asx.guard_version_immutable()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'scenario_versions là bất biến: không được xoá (version %)', OLD.id
      USING ERRCODE = 'integrity_constraint_violation';
  END IF;
  IF NEW.scenario_id          IS DISTINCT FROM OLD.scenario_id
  OR NEW.version_number       IS DISTINCT FROM OLD.version_number
  OR NEW.schema_version       IS DISTINCT FROM OLD.schema_version
  OR NEW.content              IS DISTINCT FROM OLD.content
  OR NEW.content_hash         IS DISTINCT FROM OLD.content_hash
  OR NEW.source_type          IS DISTINCT FROM OLD.source_type
  OR NEW.parent_version_id    IS DISTINCT FROM OLD.parent_version_id
  OR NEW.validation_report_id IS DISTINCT FROM OLD.validation_report_id
  OR NEW.published_by         IS DISTINCT FROM OLD.published_by
  OR NEW.published_at         IS DISTINCT FROM OLD.published_at THEN
    RAISE EXCEPTION 'scenario_versions là bất biến: chỉ được đổi trạng thái (version %)', OLD.id
      USING ERRCODE = 'integrity_constraint_violation';
  END IF;
  IF OLD.status = 'DEPRECATED' AND NEW.status = 'READY' THEN
    RAISE EXCEPTION 'Không thể chuyển version đã DEPRECATED về READY'
      USING ERRCODE = 'check_violation';
  END IF;
  RETURN NEW;
END $$;

CREATE TRIGGER trg_versions_immutable
  BEFORE UPDATE OR DELETE ON asx.scenario_versions
  FOR EACH ROW EXECUTE FUNCTION asx.guard_version_immutable();

-- Trigger: chỉ publish khi có validation report HỢP LỆ và KHỚP hash nội dung
CREATE OR REPLACE FUNCTION asx.guard_version_validated()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE r asx.validation_reports%ROWTYPE;
BEGIN
  SELECT * INTO r FROM asx.validation_reports WHERE id = NEW.validation_report_id;
  IF NOT r.is_valid THEN
    RAISE EXCEPTION 'Không thể publish: validation report % không hợp lệ', r.id
      USING ERRCODE = 'check_violation';
  END IF;
  IF r.scenario_id <> NEW.scenario_id OR r.content_hash <> NEW.content_hash THEN
    RAISE EXCEPTION 'Không thể publish: nội dung đã thay đổi sau khi validate'
      USING ERRCODE = 'check_violation';
  END IF;
  RETURN NEW;
END $$;

CREATE TRIGGER trg_versions_validated
  BEFORE INSERT ON asx.scenario_versions
  FOR EACH ROW EXECUTE FUNCTION asx.guard_version_validated();

-- ---------------------------------------------------------------------
-- 7. scenario_drafts - bản nháp đang chỉnh sửa (tối đa 1 / scenario)
-- ---------------------------------------------------------------------
CREATE TABLE asx.scenario_drafts (
  scenario_id                uuid PRIMARY KEY REFERENCES asx.scenarios(id) ON DELETE CASCADE,
  content                    jsonb NOT NULL CHECK (jsonb_typeof(content) = 'object'),
  base_version_id            uuid REFERENCES asx.scenario_versions(id),  -- sửa từ version nào (để diff)
  last_validation_report_id  uuid REFERENCES asx.validation_reports(id),
  row_version                integer NOT NULL DEFAULT 1,                -- optimistic concurrency (EF Core)
  updated_by                 uuid NOT NULL REFERENCES asx.app_users(id),
  created_at                 timestamptz NOT NULL DEFAULT now(),
  updated_at                 timestamptz NOT NULL DEFAULT now()
);
CREATE TRIGGER trg_drafts_updated_at BEFORE UPDATE ON asx.scenario_drafts
  FOR EACH ROW EXECUTE FUNCTION asx.set_updated_at();

-- ---------------------------------------------------------------------
-- 8. Tag (tổ chức repository - ở mức scenario, không sinh version mới)
-- ---------------------------------------------------------------------
CREATE TABLE asx.tags (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name        text NOT NULL CHECK (name ~ '^[a-z0-9][a-z0-9_-]{0,31}$'),
  created_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_tags_name UNIQUE (name)
);

CREATE TABLE asx.scenario_tags (
  scenario_id  uuid NOT NULL REFERENCES asx.scenarios(id) ON DELETE CASCADE,
  tag_id       uuid NOT NULL REFERENCES asx.tags(id) ON DELETE CASCADE,
  PRIMARY KEY (scenario_id, tag_id)
);
CREATE INDEX ix_scenario_tags_tag ON asx.scenario_tags (tag_id);

-- ---------------------------------------------------------------------
-- 9. import_records - mỗi lần import (kể cả thất bại)
-- ---------------------------------------------------------------------
CREATE TABLE asx.import_records (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  scenario_id         uuid REFERENCES asx.scenarios(id),         -- NULL nếu import thất bại
  format              text NOT NULL CHECK (format IN ('json','yaml','carla_route_xml','openscenario')),
  original_file_name  text NOT NULL,
  original_hash       text NOT NULL CHECK (original_hash ~ '^sha256:[a-f0-9]{64}$'),
  storage_path        text NOT NULL,          -- file gốc trong Supabase Storage (bucket scenario-imports)
  status              text NOT NULL CHECK (status IN ('SUCCEEDED','FAILED')),
  import_report       jsonb NOT NULL DEFAULT '{}'::jsonb,   -- mapped / ignored / defaulted / warnings
  created_by          uuid NOT NULL REFERENCES asx.app_users(id),
  created_at          timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_import_scenario CHECK ((status = 'SUCCEEDED') = (scenario_id IS NOT NULL))
);
CREATE INDEX ix_import_records_scenario ON asx.import_records (scenario_id);

-- ---------------------------------------------------------------------
-- 10. audit_logs - nhật ký thao tác (traceability)
-- ---------------------------------------------------------------------
CREATE TABLE asx.audit_logs (
  id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  entity_type  text NOT NULL CHECK (entity_type IN ('SCENARIO','DRAFT','VERSION','IMPORT','VARIANT_BATCH','MAP_METADATA')),
  entity_id    uuid NOT NULL,
  action       text NOT NULL CHECK (action IN
               ('CREATE','UPDATE_DRAFT','DISCARD_DRAFT','VALIDATE','PUBLISH','CLONE','IMPORT',
                'EXPORT','GENERATE_VARIANTS','DEPRECATE','ARCHIVE','RESTORE','TAG','UNTAG')),
  actor_id     uuid NOT NULL REFERENCES asx.app_users(id),
  details      jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_audit_entity ON asx.audit_logs (entity_type, entity_id, created_at DESC);

-- ---------------------------------------------------------------------
-- 11. Khoá ngoại vòng (tạo sau khi đủ bảng)
-- ---------------------------------------------------------------------
ALTER TABLE asx.scenarios
  ADD CONSTRAINT fk_scenarios_latest_version FOREIGN KEY (latest_version_id)
      REFERENCES asx.scenario_versions(id) DEFERRABLE INITIALLY DEFERRED,
  ADD CONSTRAINT fk_scenarios_origin_version FOREIGN KEY (origin_version_id)
      REFERENCES asx.scenario_versions(id);

ALTER TABLE asx.variant_batches
  ADD CONSTRAINT fk_variant_batches_base FOREIGN KEY (base_version_id)
      REFERENCES asx.scenario_versions(id);

ALTER TABLE asx.scenario_versions
  ADD CONSTRAINT fk_versions_import FOREIGN KEY (import_record_id)
      REFERENCES asx.import_records(id);

-- ---------------------------------------------------------------------
-- 12. View tiện dụng
-- ---------------------------------------------------------------------
-- Danh sách cho trang Repository
CREATE VIEW asx.v_scenario_repository AS
SELECT s.id, s.code, s.name, s.category, s.status,
       s.latest_version_number,
       v.id           AS latest_version_id,
       v.status       AS latest_version_status,
       v.map_name, v.weather_preset, v.event_types, v.event_count,
       (d.scenario_id IS NOT NULL) AS has_draft,
       COALESCE((SELECT array_agg(t.name ORDER BY t.name)
                 FROM asx.scenario_tags st JOIN asx.tags t ON t.id = st.tag_id
                 WHERE st.scenario_id = s.id), '{}') AS tags,
       s.created_by, s.created_at, s.updated_at
FROM asx.scenarios s
LEFT JOIN asx.scenario_versions v ON v.id = s.latest_version_id
LEFT JOIN asx.scenario_drafts   d ON d.scenario_id = s.id;

-- ĐIỂM BÀN GIAO CHO FLOW 2: các version được phép đưa vào campaign mới
CREATE VIEW asx.v_ready_scenario_versions AS
SELECT v.id AS scenario_version_id, s.id AS scenario_id, s.code, s.name, s.category,
       v.version_number, v.map_name, v.weather_preset, v.event_types, v.event_count,
       v.actor_count, v.npc_vehicle_count, v.pedestrian_count, v.speed_limit_kmh, v.ego_blueprint,
       v.content, v.content_hash, v.published_at
FROM asx.scenario_versions v
JOIN asx.scenarios s ON s.id = v.scenario_id
WHERE v.status = 'READY' AND s.status = 'ACTIVE';

-- ---------------------------------------------------------------------
-- 13. Bảo mật trên Supabase
--  Schema asx KHÔNG thêm vào "Exposed schemas" của Data API.
--  Bật RLS (không có policy) để chặn mọi truy cập từ anon/authenticated
--  nếu lỡ expose. Backend kết nối bằng role postgres/owner -> bỏ qua RLS.
-- ---------------------------------------------------------------------
DO $$
DECLARE t text;
BEGIN
  FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'asx' LOOP
    EXECUTE format('ALTER TABLE asx.%I ENABLE ROW LEVEL SECURITY', t);
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA asx FROM anon';
    EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA asx FROM anon';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA asx FROM authenticated';
    EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA asx FROM authenticated';
  END IF;
END $$;

-- ---------------------------------------------------------------------
-- Ghi chú Supabase Auth (tuỳ chọn, quyết định ở Flow Admin):
--   Nếu dùng Supabase Auth để đăng nhập, liên kết user bằng:
--   ALTER TABLE asx.app_users
--     ADD CONSTRAINT fk_app_users_auth FOREIGN KEY (id)
--     REFERENCES auth.users(id) ON DELETE CASCADE;
--   ASP.NET Core xác thực JWT do Supabase phát hành (claim "sub" = id).
-- ---------------------------------------------------------------------
