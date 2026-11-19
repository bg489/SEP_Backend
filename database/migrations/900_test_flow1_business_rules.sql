-- =====================================================================
-- AutoScenarioX - Test luật nghiệp vụ Flow 1 (chạy được trong Supabase SQL Editor)
--
-- * Toàn bộ dữ liệu test được ROLLBACK ở cuối => không để lại rác trong DB.
-- * Kết quả trả về dạng bảng: cột "result" phải là PASS ở mọi dòng.
-- * Áp dụng cho giai đoạn hiện tại (mới chạy migration 001, chưa liên kết Supabase Auth).
-- =====================================================================
CREATE OR REPLACE FUNCTION pg_temp.run_flow1_tests()
RETURNS TABLE (test_id text, description text, expected text, actual text, result text)
LANGUAGE plpgsql AS $$
DECLARE
  ids   text[] := '{}';
  descs text[] := '{}';
  exps  text[] := '{}';
  acts  text[] := '{}';
  msg   text;
  u     uuid := 'aaaaaaaa-0000-0000-0000-000000000001';
  s     uuid := 'aaaaaaaa-0000-0000-0000-000000000002';
  rep_ok  uuid := 'aaaaaaaa-0000-0000-0000-00000000000a';
  rep_bad uuid := 'aaaaaaaa-0000-0000-0000-00000000000b';
  v1    uuid := 'aaaaaaaa-0000-0000-0000-000000000003';
  hash_a text := 'sha256:' || repeat('a', 64);
  hash_b text := 'sha256:' || repeat('b', 64);
  content_v1 jsonb := '{
    "map": {"name": "Town05"},
    "route": {"start": {"spawn_point_index": 12}, "goal": {"spawn_point_index": 87}, "speed_limit_kmh": 40},
    "weather": {"preset": "HardRainNoon"},
    "ego_vehicle": {"blueprint": "vehicle.tesla.model3"},
    "traffic": {"npc_vehicles": {"count": 10}, "pedestrians": {"count": 5}},
    "actors": [{"id": "ped_01"}],
    "events": [{"type": "pedestrian_crossing"}]
  }';
BEGIN
  -- ========== Khối ngoài: mọi thay đổi sẽ bị rollback ở cuối ==========
  BEGIN
    INSERT INTO asx.app_users (id, email, display_name, role)
    VALUES (u, 'test.engineer@example.com', 'Test SE', 'SCENARIO_ENGINEER');
    INSERT INTO asx.scenarios (id, name, category, origin_type, created_by)
    VALUES (s, 'TEST Pedestrian Crossing', 'pedestrian', 'manual', u);
    INSERT INTO asx.scenario_drafts (scenario_id, content, updated_by) VALUES (s, content_v1, u);
    INSERT INTO asx.validation_reports (id, scenario_id, content_hash, is_valid, schema_version, validator_version, created_by)
    VALUES (rep_ok, s, hash_a, true, '1.0', 'test', u);
    INSERT INTO asx.validation_reports (id, scenario_id, content_hash, is_valid, stopped_at, errors, schema_version, validator_version, created_by)
    VALUES (rep_bad, s, hash_b, false, 'schema', '[{"path":"/map"}]', '1.0', 'test', u);

    -- T01
    BEGIN
      INSERT INTO asx.scenario_versions (scenario_id, version_number, schema_version, content, content_hash, source_type, validation_report_id, published_by)
      VALUES (s, 1, '1.0', content_v1, hash_b, 'manual', rep_bad, u);
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T01')::text); descs := array_append(descs, ('Publish với validation report KHÔNG hợp lệ')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    -- T02
    BEGIN
      INSERT INTO asx.scenario_versions (scenario_id, version_number, schema_version, content, content_hash, source_type, validation_report_id, published_by)
      VALUES (s, 1, '1.0', content_v1, 'sha256:' || repeat('c', 64), 'manual', rep_ok, u);
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T02')::text); descs := array_append(descs, ('Publish nội dung khác với nội dung đã validate')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    -- T03: quy trình publish đúng
    BEGIN
      PERFORM 1 FROM asx.scenarios WHERE id = s FOR UPDATE;
      INSERT INTO asx.scenario_versions (id, scenario_id, version_number, schema_version, content, content_hash, source_type, validation_report_id, published_by)
      VALUES (v1, s, 1, '1.0', content_v1, hash_a, 'manual', rep_ok, u);
      UPDATE asx.scenarios SET latest_version_id = v1, latest_version_number = 1 WHERE id = s;
      DELETE FROM asx.scenario_drafts WHERE scenario_id = s;
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T03')::text); descs := array_append(descs, ('Publish đúng quy trình (validate hợp lệ, cùng hash)')::text); exps := array_append(exps, ('OK')::text); acts := array_append(acts, (msg)::text);

    -- T04: cột generated
    SELECT concat_ws('|', map_name, weather_preset, event_types::text, actor_count, event_count,
                     npc_vehicle_count, pedestrian_count, speed_limit_kmh, ego_blueprint)
      INTO msg FROM asx.scenario_versions WHERE id = v1;
    ids := array_append(ids, ('T04')::text); descs := array_append(descs, ('Cột generated tự tách cấu hình từ content')::text);
    exps := array_append(exps, ('Town05|HardRainNoon|["pedestrian_crossing"]|1|1|10|5|40|vehicle.tesla.model3')::text); acts := array_append(acts, (coalesce(msg, 'NULL'))::text);

    -- T05
    SELECT code INTO msg FROM asx.scenarios WHERE id = s;
    ids := array_append(ids, ('T05')::text); descs := array_append(descs, ('Mã scenario tự sinh dạng SCN-xxxxx')::text); exps := array_append(exps, ('MATCH SCN-')::text); 
    acts := array_append(acts, (CASE WHEN msg ~ '^SCN-[0-9]{5,}$' THEN 'MATCH SCN-' ELSE coalesce(msg, 'NULL') END)::text);

    -- T06
    BEGIN
      UPDATE asx.scenario_versions SET content = '{"map":{"name":"Town01"}}' WHERE id = v1;
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T06')::text); descs := array_append(descs, ('Sửa content của version đã publish')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    -- T07
    BEGIN
      DELETE FROM asx.scenario_versions WHERE id = v1;
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T07')::text); descs := array_append(descs, ('Xoá version đã publish')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    -- T08
    BEGIN
      INSERT INTO asx.scenario_versions (scenario_id, version_number, schema_version, content, content_hash, source_type, validation_report_id, published_by)
      VALUES (s, 2, '1.0', content_v1, hash_a, 'manual', rep_ok, u);
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T08')::text); descs := array_append(descs, ('Publish version mới y hệt version cũ (trùng hash)')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    -- T09
    SELECT count(*)::text INTO msg FROM asx.v_ready_scenario_versions WHERE scenario_id = s;
    ids := array_append(ids, ('T09')::text); descs := array_append(descs, ('Flow 2 thấy version READY qua view bàn giao')::text); exps := array_append(exps, ('1')::text); acts := array_append(acts, (msg)::text);

    -- T10
    BEGIN
      UPDATE asx.scenario_versions SET status = 'DEPRECATED', deprecated_at = now(), deprecated_by = u WHERE id = v1;
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T10')::text); descs := array_append(descs, ('Deprecate version (chỉ đổi trạng thái)')::text); exps := array_append(exps, ('OK')::text); acts := array_append(acts, (msg)::text);

    -- T11
    BEGIN
      UPDATE asx.scenario_versions SET status = 'READY', deprecated_at = NULL, deprecated_by = NULL WHERE id = v1;
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T11')::text); descs := array_append(descs, ('Đưa version DEPRECATED về READY')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    -- T12
    SELECT count(*)::text INTO msg FROM asx.v_ready_scenario_versions WHERE scenario_id = s;
    ids := array_append(ids, ('T12')::text); descs := array_append(descs, ('Version DEPRECATED biến mất khỏi view bàn giao')::text); exps := array_append(exps, ('0')::text); acts := array_append(acts, (msg)::text);

    -- T13
    BEGIN
      INSERT INTO asx.scenarios (name, category, origin_type, created_by) VALUES ('TEST clone thiếu cha', 'pedestrian', 'clone', u);
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T13')::text); descs := array_append(descs, ('Tạo scenario clone mà không có version cha')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    -- T14
    BEGIN
      INSERT INTO asx.validation_reports (scenario_id, content_hash, is_valid, errors, schema_version, validator_version, created_by)
      VALUES (s, hash_a, true, '[{"path":"/x"}]', '1.0', 'test', u);
      msg := 'OK';
    EXCEPTION WHEN OTHERS THEN msg := 'ERROR: ' || SQLERRM; END;
    ids := array_append(ids, ('T14')::text); descs := array_append(descs, ('Report ghi hợp lệ nhưng lại có lỗi (mâu thuẫn)')::text); exps := array_append(exps, ('ERROR')::text); acts := array_append(acts, (msg)::text);

    RAISE EXCEPTION 'ROLLBACK_TEST_DATA';
  EXCEPTION WHEN raise_exception THEN
    IF SQLERRM <> 'ROLLBACK_TEST_DATA' THEN RAISE; END IF;
  END;

  -- ========== Kiểm tra cấu hình bảo mật (không cần dữ liệu) ==========
  SELECT count(*)::text INTO msg
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname = 'asx' AND c.relkind = 'r' AND NOT c.relrowsecurity;
  ids := array_append(ids, ('T15')::text); descs := array_append(descs, ('Số bảng trong asx CHƯA bật RLS')::text); exps := array_append(exps, ('0')::text); acts := array_append(acts, (msg)::text);

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    msg := has_schema_privilege('anon', 'asx', 'USAGE')::text;
  ELSE
    msg := 'false';  -- không phải môi trường Supabase
  END IF;
  ids := array_append(ids, ('T16')::text); descs := array_append(descs, ('Role anon (public API) có quyền vào schema asx?')::text); exps := array_append(exps, ('false')::text); acts := array_append(acts, (msg)::text);

  SELECT count(*)::text INTO msg FROM asx.scenarios WHERE name LIKE 'TEST %';
  ids := array_append(ids, ('T17')::text); descs := array_append(descs, ('Dữ liệu test đã được dọn sạch')::text); exps := array_append(exps, ('0')::text); acts := array_append(acts, (msg)::text);

  RETURN QUERY
  SELECT t.i, t.d, t.e, t.a,
         CASE WHEN (t.e = 'ERROR' AND t.a LIKE 'ERROR%') OR t.e = t.a THEN 'PASS' ELSE 'FAIL' END
  FROM unnest(ids, descs, exps, acts) AS t(i, d, e, a);
END $$;

SELECT * FROM pg_temp.run_flow1_tests();
