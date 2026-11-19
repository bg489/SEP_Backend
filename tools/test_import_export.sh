#!/usr/bin/env bash
# Kiểm thử B11 - Import/Export JSON+YAML.
#   export ASX_TOKEN="<access token>"      # bỏ qua nếu Auth:Mode = Dev
#   tools/test_import_export.sh
#
# Script tự archive mọi scenario nó tạo ra, không để lại rác.
set -u
API="${ASX_API_BASE:-https://localhost:7080}"
TOKEN="${ASX_TOKEN:-}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
TMP="$(mktemp -d)"
CREATED=()

pass=0; fail=0
check() {
  if [ "$2" = "$3" ]; then printf '  PASS  %-52s %s\n' "$1" "$3"; pass=$((pass+1))
  else printf '  FAIL  %-52s mong đợi %s, nhận %s\n' "$1" "$2" "$3"; fail=$((fail+1)); fi
}
auth() { [ -n "$TOKEN" ] && printf 'Authorization: Bearer %s' "$TOKEN" || printf 'X-No-Auth: 1'; }
api()  { curl -k -s -H "$(auth)" "$@"; }
code() { curl -k -s -o /dev/null -w '%{http_code}' -H "$(auth)" "$@"; }

cleanup() {
  for id in "${CREATED[@]:-}"; do
    [ -n "$id" ] && api -o /dev/null -X POST "$API/api/v1/scenarios/$id/archive"
  done
  rm -rf "$TMP"
  echo "Đã archive ${#CREATED[@]} scenario test."
}
trap cleanup EXIT

echo "=== T1. Import YAML ==="
api -X POST "$API/api/v1/scenarios/import" \
    -F "file=@$REPO/examples/S24_pedestrian_night_fog.yaml" > "$TMP/t1.json"
check "định dạng nhận ra là yaml" yaml "$(jq -r '.format // "?"' "$TMP/t1.json")"
check "scenario được tạo"          true  "$(jq -r '(.scenario.id != null)' "$TMP/t1.json")"
check "originType = import"        import "$(jq -r '.scenario.originType // "?"' "$TMP/t1.json")"
check "có bản nháp"                true  "$(jq -r '.scenario.hasDraft' "$TMP/t1.json")"
check "chưa publish version nào"   null  "$(jq -r '.scenario.latestVersion' "$TMP/t1.json")"
defaulted=$(jq -r '.report.defaulted | length' "$TMP/t1.json")
check "có trường được điền mặc định" true "$([ "$defaulted" -gt 0 ] && echo true || echo false)"
echo "     defaulted: $(jq -r '[.report.defaulted[].path] | join(", ")' "$TMP/t1.json")"
YAML_ID=$(jq -r '.scenario.id' "$TMP/t1.json"); CREATED+=("$YAML_ID")

echo "=== T2. Import JSON ==="
api -X POST "$API/api/v1/scenarios/import" \
    -F "file=@$REPO/examples/S23_pedestrian_crossing_rain.json" > "$TMP/t2.json"
check "định dạng nhận ra là json" json "$(jq -r '.format // "?"' "$TMP/t2.json")"
check "original_hash đúng dạng"   true \
  "$(jq -r '.originalHash | test("^sha256:[a-f0-9]{64}$")' "$TMP/t2.json")"
JSON_ID=$(jq -r '.scenario.id' "$TMP/t2.json"); CREATED+=("$JSON_ID")

echo "=== T3. Import file sai luật nghiệp vụ -> 422, KHÔNG tạo scenario ==="
before=$(api "$API/api/v1/scenarios?pageSize=1" | jq -r '.totalCount')
api -X POST "$API/api/v1/scenarios/import" \
    -F "file=@$REPO/examples/INVALID_tier2_business.json" > "$TMP/t3.json"
check "mã lỗi" IMPORT_VALIDATION_FAILED "$(jq -r '.code // "?"' "$TMP/t3.json")"
check "report dừng ở tầng business" business "$(jq -r '.report.stoppedAt // "?"' "$TMP/t3.json")"
check "report có 8 lỗi" 8 "$(jq -r '.report.errors | length' "$TMP/t3.json")"
after=$(api "$API/api/v1/scenarios?pageSize=1" | jq -r '.totalCount')
check "số scenario không đổi" "$before" "$after"

echo "=== T4. Đuôi file lạ -> 422 ==="
echo '{"schema_version":"1.0"}' > "$TMP/khong-biet.txt"
api -X POST "$API/api/v1/scenarios/import" -F "file=@$TMP/khong-biet.txt" > "$TMP/t4.json"
check "mã lỗi" IMPORT_FORMAT_UNSUPPORTED "$(jq -r '.code // "?"' "$TMP/t4.json")"

echo "=== T5. Ép format cho file đuôi lạ ==="
cp "$REPO/examples/S23_pedestrian_crossing_rain.json" "$TMP/khong-duoi"
api -X POST "$API/api/v1/scenarios/import" \
    -F "file=@$TMP/khong-duoi" -F "format=json" -F "name=Ep format json" > "$TMP/t5.json"
check "ép format=json chạy được" json "$(jq -r '.format // "?"' "$TMP/t5.json")"
check "tên bị ghi đè" "Ep format json" "$(jq -r '.scenario.name // "?"' "$TMP/t5.json")"
FORCED_ID=$(jq -r '.scenario.id' "$TMP/t5.json"); CREATED+=("$FORCED_ID")

echo "=== T6. Export version của scenario seed ==="
SEED_ID=d1000000-0000-0000-0000-000000000001
check "export json" 200 "$(code "$API/api/v1/scenarios/$SEED_ID/versions/1/export")"
check "export yaml" 200 "$(code "$API/api/v1/scenarios/$SEED_ID/versions/1/export?format=yaml")"
check "export định dạng lạ -> 422" 422 "$(code "$API/api/v1/scenarios/$SEED_ID/versions/1/export?format=xml")"
api "$API/api/v1/scenarios/$SEED_ID/versions/1/export?format=yaml" > "$TMP/seed_v1.yaml"
check "file yaml có nội dung" true "$([ -s "$TMP/seed_v1.yaml" ] && echo true || echo false)"

echo "=== T7. Vòng tròn export YAML -> import -> publish: content_hash phải giữ nguyên ==="
ORIG_HASH=$(api "$API/api/v1/scenarios/$SEED_ID/versions/1" | jq -r '.summary.contentHash')
api -X POST "$API/api/v1/scenarios/import" \
    -F "file=@$TMP/seed_v1.yaml" -F "name=Vong tron YAML" > "$TMP/t7.json"
RT_ID=$(jq -r '.scenario.id // empty' "$TMP/t7.json")
if [ -z "$RT_ID" ]; then
  echo "  FAIL  import vòng tròn: $(cat "$TMP/t7.json")"; fail=$((fail+1))
else
  CREATED+=("$RT_ID")
  api -X POST "$API/api/v1/scenarios/$RT_ID/versions" \
      -H 'Content-Type: application/json' \
      -d '{"changeNote":"publish sau khi import vong tron"}' > "$TMP/t7pub.json"
  check "publish được ngay (không phải validate lại)" 1 "$(jq -r '.summary.versionNumber // 0' "$TMP/t7pub.json")"
  check "content_hash giữ nguyên qua YAML" "$ORIG_HASH" "$(jq -r '.summary.contentHash // "?"' "$TMP/t7pub.json")"
  check "sourceType = import" import "$(jq -r '.summary.sourceType // "?"' "$TMP/t7pub.json")"
fi

echo "=== T8. Lịch sử import ==="
api "$API/api/v1/scenarios/$YAML_ID/imports" > "$TMP/t8.json"
check "có 1 dòng import"   1 "$(jq -r 'length' "$TMP/t8.json")"
check "trạng thái SUCCEEDED" SUCCEEDED "$(jq -r '.[0].status // "?"' "$TMP/t8.json")"

echo
echo "Tổng: $pass đạt, $fail hỏng"
[ "$fail" -eq 0 ]
