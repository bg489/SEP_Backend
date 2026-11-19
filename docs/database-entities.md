// 24 bảng schema asx sau migration 117. Dán nguyên file vào https://dbdiagram.io
// Bốn nhóm đi từ trái sang phải: người dùng, kịch bản, campaign, oracle, phân tích.
// Đường kẻ chỉ là quan hệ dữ liệu. Các cột created_by, published_by, updated_by,
// archived_by, deprecated_by, imported_by vẫn là uuid trỏ app_users.id nhưng không vẽ,
// kẻ ra thì mọi bảng bị kéo về một chỗ.

TableGroup nguoi_dung [color: #5C6B73] {
  app_users
  audit_logs
}

TableGroup kich_ban [color: #0F6E56] {
  map_metadata
  tags
  scenarios
  scenario_drafts
  scenario_versions
  scenario_tags
  validation_reports
  import_records
  variant_batches
}

TableGroup campaign [color: #185FA5] {
  oracle_configs
  campaigns
  campaign_versions
  simulation_runs
  simulation_workers
}

TableGroup oracle [color: #854F0B] {
  run_telemetry
  evaluations
  violations
}

TableGroup phan_tich [color: #993C1D] {
  failure_analyses
  failure_faults
  failure_fault_runs
  similarity_analyses
  system_settings
}

Table app_users {
  id uuid [pk]
  email text [not null]
  display_name text [not null]
  role text [not null]
  is_active boolean [not null]
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
}

Table audit_logs {
  id bigint [pk]
  entity_type text [not null]
  entity_id uuid [not null]
  action text [not null]
  actor_id uuid [not null, ref: > app_users.id]
  details jsonb [not null]
  created_at timestamptz [not null]
}

Table map_metadata {
  id uuid [pk]
  map_name text [not null]
  carla_version text [not null]
  spawn_points jsonb [not null]
  spawn_point_count integer
  preview_image_path text
  is_active boolean [not null]
  imported_by uuid
  imported_at timestamptz [not null]

  indexes {
    (map_name, carla_version) [unique]
  }
}

Table tags {
  id uuid [pk]
  name text [not null, unique]
  created_at timestamptz [not null]
}

Table scenarios {
  id uuid [pk]
  seq_no bigint [not null]
  code text [unique]
  name text [not null]
  description text
  category text [not null]
  status text [not null]
  origin_type text [not null]
  origin_version_id uuid [ref: > scenario_versions.id]
  variant_batch_id uuid [ref: > variant_batches.id]
  latest_version_id uuid [ref: > scenario_versions.id]
  latest_version_number integer [not null]
  created_by uuid [not null]
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
  archived_by uuid
  archived_at timestamptz
}

Table scenario_drafts {
  scenario_id uuid [pk, ref: > scenarios.id]
  content jsonb [not null]
  base_version_id uuid [ref: > scenario_versions.id]
  last_validation_report_id uuid [ref: > validation_reports.id]
  row_version integer [not null]
  updated_by uuid [not null]
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
}

Table scenario_versions {
  id uuid [pk]
  scenario_id uuid [not null, ref: > scenarios.id]
  version_number integer [not null]
  schema_version text [not null]
  content jsonb [not null]
  content_hash text [not null]
  map_name text
  weather_preset text
  event_types jsonb
  actor_count integer
  event_count integer
  npc_vehicle_count integer
  pedestrian_count integer
  speed_limit_kmh numeric
  ego_blueprint text
  status text [not null]
  change_note text
  source_type text [not null]
  parent_version_id uuid [ref: > scenario_versions.id]
  import_record_id uuid [ref: > import_records.id]
  validation_report_id uuid [not null, ref: > validation_reports.id]
  published_by uuid [not null]
  published_at timestamptz [not null]
  deprecated_by uuid
  deprecated_at timestamptz

  indexes {
    (scenario_id, content_hash) [unique]
    (scenario_id, version_number) [unique]
  }
}

Table scenario_tags {
  scenario_id uuid [pk, ref: > scenarios.id]
  tag_id uuid [pk, ref: > tags.id]
}

Table validation_reports {
  id uuid [pk]
  scenario_id uuid [not null, ref: > scenarios.id]
  content_hash text [not null]
  is_valid boolean [not null]
  stopped_at text
  errors jsonb [not null]
  warnings jsonb [not null]
  schema_version text [not null]
  validator_version text [not null]
  map_metadata_id uuid [ref: > map_metadata.id]
  created_by uuid [not null]
  created_at timestamptz [not null]
}

Table import_records {
  id uuid [pk]
  scenario_id uuid [ref: > scenarios.id]
  format text [not null]
  original_file_name text [not null]
  original_hash text [not null]
  storage_path text
  status text [not null]
  import_report jsonb [not null]
  created_by uuid [not null]
  created_at timestamptz [not null]
  original_content text
}

Table variant_batches {
  id uuid [pk]
  base_version_id uuid [not null, ref: > scenario_versions.id]
  parameter_grid jsonb [not null]
  requested_count integer [not null]
  generated_count integer [not null]
  note text
  created_by uuid [not null]
  created_at timestamptz [not null]
  strategy text [not null]
}

Table oracle_configs {
  id uuid [pk]
  version integer [not null, unique]
  name text [not null]
  description text
  speeding_tolerance_kmh numeric [not null]
  speeding_min_duration_s numeric [not null]
  freezing_max_still_s numeric [not null]
  ttc_threshold_s numeric [not null]
  route_goal_tolerance_m numeric [not null]
  created_by uuid
  created_at timestamptz [not null]
}

Table campaigns {
  id uuid [pk]
  seq_no bigint [not null, unique]
  code text
  name text [not null]
  description text
  strategy text [not null]
  status text [not null]
  progress integer [not null]
  pass_rate numeric [not null]
  failed_runs integer [not null]
  estimated_duration text [not null]
  created_by uuid [not null]
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
  sut_type text [not null]
  sut_behavior text [not null]
  sut_carla_version text [not null]
  started_at timestamptz
  oracle_config_id uuid [not null, ref: > oracle_configs.id]
}

Table campaign_versions {
  campaign_id uuid [pk, ref: > campaigns.id]
  scenario_version_id uuid [pk, ref: > scenario_versions.id]
  sort_order integer [not null]
}

Table simulation_runs {
  id uuid [pk]
  campaign_id uuid [not null, ref: > campaigns.id]
  scenario_version_id uuid [not null, ref: > scenario_versions.id]
  sort_order integer [not null]
  status text [not null]
  verdict text
  content_hash text
  seed integer
  started_at timestamptz
  finished_at timestamptz
  error_message text
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
  infra_retry_count integer [not null]
  attempt integer [not null]
  previous_run_id uuid [ref: > simulation_runs.id]
  is_current boolean [not null]
  worker_id uuid [ref: > simulation_workers.id]
  telemetry_sha256 text
  progress_json jsonb
  progress_at timestamptz

  indexes {
    (campaign_id, scenario_version_id, attempt) [unique]
  }
  Note: 'SQL also enforces one current attempt per campaign/version with a partial unique index WHERE is_current.'
}

Table run_telemetry {
  run_id uuid [pk, ref: > simulation_runs.id]
  telemetry_path text [not null]
  outcome text
  duration_s numeric
  received_at timestamptz [not null]
  video_path text
  video_sha256 text
}

Table simulation_workers {
  id uuid [pk]
  owner_user_id uuid [not null, ref: > app_users.id]
  name text [not null]
  host text [not null]
  rpc_port integer [not null]
  capacity integer [not null]
  enabled boolean [not null]
  quality text [not null, note: 'Low only']
  carla_version text [not null]
  maps_json jsonb [not null]
  last_heartbeat_at timestamptz
  last_error text
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
  indexes { (owner_user_id, name) [unique] }
}

Table system_settings {
  id integer [pk, note: 'Singleton, id = 1']
  active_oracle_config_id uuid [ref: > oracle_configs.id]
  similarity_warning_percent numeric [not null]
  similarity_block_percent numeric [not null]
  retention_days integer [not null]
  retention_enabled boolean [not null]
  updated_by uuid [ref: > app_users.id]
  updated_at timestamptz [not null]
}

Table similarity_analyses {
  id uuid [pk]
  method text [not null, note: 'gower or dtw']
  result_json jsonb [not null]
  created_by uuid [ref: > app_users.id]
  created_at timestamptz [not null]
}

Table evaluations {
  id uuid [pk]
  run_id uuid [not null, ref: > simulation_runs.id]
  oracle_config_id uuid [not null, ref: > oracle_configs.id]
  verdict text [not null]
  error_reason text
  evaluated_at timestamptz [not null]

  indexes {
    (run_id, oracle_config_id) [unique]
  }
}

Table violations {
  id uuid [pk]
  evaluation_id uuid [not null, ref: > evaluations.id]
  check_name text [not null]
  severity text [not null]
  t numeric [not null]
  duration_s numeric
  details jsonb [not null]
  created_at timestamptz [not null]
}

Table failure_analyses {
  id uuid [pk]
  campaign_id uuid [not null, unique, ref: > campaigns.id]
  status text [not null]
  summary text [not null]
  review_note text
  effectiveness jsonb
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
}

Table failure_faults {
  id uuid [pk]
  analysis_id uuid [not null, ref: > failure_analyses.id]
  root_scenario_id uuid [not null]
  root_scenario_code text [not null]
  root_scenario_name text [not null]
  check_name text [not null]
  status text [not null]
  first_run_id uuid [not null]
  first_t numeric [not null]
  explanation text
  report_text text
}

Table failure_fault_runs {
  fault_id uuid [pk, ref: > failure_faults.id]
  run_id uuid [pk, ref: > simulation_runs.id]
  is_primary boolean [not null]
}
