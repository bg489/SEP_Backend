#!/usr/bin/env python3
"""B12 integration tests. Tạo dữ liệu riêng, archive đúng các scenario của lần test trong finally.

ASX_API_BASE=http://localhost:5080 python3 tools/test_variants.py
ASX_TOKEN dùng khi Auth:Mode=Supabase. Không dùng seed hay sửa dữ liệu có sẵn.
"""
import copy
import itertools
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid

from variant_api import Api

ROOT = Path(__file__).resolve().parents[1]
API = Api()
CREATED = []
CHECKS = 0


def check(condition, message):
    global CHECKS
    if not condition:
        raise AssertionError(message)
    CHECKS += 1
    print("PASS", message, flush=True)


def create_base(content):
    scenario, _ = API.expect("POST", "/api/v1/scenarios", 201, {
        "name": "B12 test " + uuid.uuid4().hex[:8], "category": "pedestrian"})
    sid = scenario["id"]
    CREATED.append(sid)
    path = "/api/v1/scenarios/" + sid
    draft, _ = API.expect("GET", path + "/draft", 200)
    API.expect("PUT", path + "/draft", 200, {"content": content}, {"If-Match": '"%s"' % draft["rowVersion"]})
    report, _ = API.expect("POST", path + "/draft/validate", 200)
    check(report["isValid"], "base validates")
    version, _ = API.expect("POST", path + "/versions", 201, {})
    return path, version


def generate(path, request):
    result, headers = API.expect("POST", path, 201, request)
    CREATED.extend(s["id"] for s in result["scenarios"])
    return result, headers


def run():
    content = json.loads((ROOT / "examples/S23_pedestrian_crossing_rain.json").read_text())
    grid = json.loads((ROOT / "examples/experiments/SV5_pedestrian_grid.json").read_text())
    base_path, version = create_base(content)
    endpoint = base_path + "/versions/1/variant-batches"
    result, headers = generate(endpoint, grid)
    batch = result["batch"]
    check(batch["requestedCount"] == batch["generatedCount"] == 12, "Cartesian grid produces 12 variants")
    check(batch["baseVersionId"] == version["summary"]["id"], "batch refers to immutable base version")
    check(batch["parameterGrid"] == grid["parameterGrid"], "history stores original grid")
    location = headers.get("Location") or headers.get("location")
    detail, _ = API.expect("GET", location.removeprefix(API.base), 200)
    check(detail == result, "Location retrieves the complete batch")

    expected = set(itertools.product([0, 40], [0.8, 2.0], [5, 15, 30]))
    observed, hashes = set(), set()
    for scenario in result["scenarios"]:
        path = "/api/v1/scenarios/" + scenario["id"]
        check(scenario["hasDraft"] and scenario["latestVersionId"] is None, "variant initially has a draft only")
        profile, _ = API.expect("GET", path, 200)
        check(profile["originType"] == "variant", "scenario origin is variant")
        draft, _ = API.expect("GET", path + "/draft", 200)
        document = draft["content"]
        check(draft["lastValidationReportId"] is not None, "draft has stored validation report")
        check(document["source"] == {"type": "variant", "parent_version_id": version["summary"]["id"]}, "draft provenance matches base")
        values = document["parameters"]
        observed.add(tuple(values[k]["value"] for k in ("npc_count", "pedestrian_speed_ms", "trigger_distance_m")))
        for block in ("map", "route", "weather", "ego_vehicle", "traffic", "actors", "events", "execution"):
            check(document[block] == version["content"][block], "unchanged block " + block)
        published, _ = API.expect("POST", path + "/versions", 201, {"changeNote": "B12 integration"})
        check(published["summary"]["status"] == "READY", "publish works without revalidation")
        check(published["parentVersionId"] == version["summary"]["id"] and published["summary"]["sourceType"] == "variant", "published lineage survives")
        check(published["validationReportId"] == draft["lastValidationReportId"], "publish uses original report")
        exported, _ = API.expect("GET", path + "/versions/1/export?format=json", 200)
        check(exported == published["content"], "export includes reproducible variant content")
        hashes.add(published["summary"]["contentHash"])
    check(observed == expected and len(hashes) == 12, "all combinations and distinct content hashes")
    detail, _ = API.expect("GET", endpoint + "/" + batch["id"], 200)
    check(all(s["latestVersionId"] and not s["hasDraft"] for s in detail["scenarios"]), "batch reflects published state")
    after, _ = API.expect("GET", base_path + "/versions/1", 200)
    check(after == version, "base version unchanged")

    cases = [None, {}, {"missing": [1]}, {"npc_count": []}, {"npc_count": None},
             {"npc_count": 1}, {"npc_count": ["1"]}, {"npc_count": [True]},
             {"npc_count": [1, 1.0]}, {"npc_count": [1.5]}, {"npc_count": [-1]},
             {"npc_count": [61]}, {"pedestrian_speed_ms": [1e100]},
             {"npc_count": [0] * 101},
             {"npc_count": list(range(11)), "trigger_distance_m": list(range(5, 15))}]
    for invalid in cases:
        error, _ = API.expect("POST", endpoint, 400, {"parameterGrid": invalid})
        check(error["code"] == "INVALID_REQUEST", "reject invalid grid " + str(invalid)[:80])
    for overrides in ({"namePrefix": " "}, {"namePrefix": "x" * 101}, {"note": "x" * 1001}):
        API.expect("POST", endpoint, 400, dict(grid, **overrides))
        check(True, "reject invalid batch metadata")
    API.expect("POST", base_path + "/versions/999/variant-batches", 404, grid)
    API.expect("GET", endpoint + "/" + str(uuid.uuid4()), 404)
    check(True, "missing version and batch return 404")
    batches, _ = API.expect("GET", endpoint, 200)
    check(len(batches) == 1, "invalid requests persist no batches")
    check(batches[0]["strategy"] == "cartesian" and batches[0]["cartesianCount"] == 12, "strategy defaults to cartesian")

    # B12b: pairwise phủ mọi cặp giá trị, tất định, nhận grid có tích Descartes vượt giới hạn.
    def parameter_rows(result):
        rows = []
        for scenario in result["scenarios"]:
            draft, _ = API.expect("GET", "/api/v1/scenarios/" + scenario["id"] + "/draft", 200)
            rows.append({k: v["value"] for k, v in draft["content"]["parameters"].items()})
        return rows

    def covers_all_pairs(axes, rows):
        return all(any(r[a] == x and r[b] == y for r in rows)
                   for a, b in itertools.combinations(sorted(axes), 2)
                   for x in axes[a] for y in axes[b])

    pairwise_request = dict(grid, strategy="pairwise", namePrefix="B12b pairwise")
    first, _ = generate(endpoint, pairwise_request)
    check(first["batch"]["strategy"] == "pairwise" and first["batch"]["cartesianCount"] == 12, "pairwise batch records strategy")
    check(first["batch"]["requestedCount"] == first["batch"]["generatedCount"] == 6, "pairwise SV5 grid produces 6 variants")
    first_rows = parameter_rows(first)
    check(covers_all_pairs(grid["parameterGrid"], first_rows), "pairwise SV5 variants cover every value pair")
    second, _ = generate(endpoint, pairwise_request)
    check(parameter_rows(second) == first_rows, "pairwise generation is deterministic")

    large = {"npc_count": [0, 10, 20, 30, 40], "pedestrian_speed_ms": [0.5, 1, 1.5, 2, 2.5],
             "trigger_distance_m": [5, 10, 15, 20, 25]}
    API.expect("POST", endpoint, 400, {"parameterGrid": large})
    check(True, "cartesian 125 combinations exceeds limit")
    big, _ = generate(endpoint, {"parameterGrid": large, "strategy": "pairwise"})
    check(big["batch"]["cartesianCount"] == 125 and big["batch"]["generatedCount"] <= 100, "pairwise accepts grid larger than limit")
    check(covers_all_pairs(large, parameter_rows(big)), "pairwise 5x5x5 variants cover every value pair")
    error, _ = API.expect("POST", endpoint, 400, dict(grid, strategy="random"))
    check("strategy" in error.get("errors", {}), "reject unknown strategy")
    error, _ = API.expect("POST", endpoint, 400, {"strategy": "pairwise",
                          "parameterGrid": {"npc_count": list(range(11)), "trigger_distance_m": list(range(5, 15))}})
    check(error["code"] == "INVALID_REQUEST", "pairwise limit uses two largest axes")
    batches, _ = API.expect("GET", endpoint, 200)
    check([b["strategy"] for b in batches].count("pairwise") == 3, "history lists pairwise batches")

    # Lỗi phụ thuộc giá trị: luật liên trường (trigger sau timeout). Ca "vượt miền schema của trường
    # dùng $param" không còn dựng được từ 2026-10-07: ParamTargetRangeRule chặn min/max vượt miền ngay
    # ở bản gốc, và grid bị giới hạn trong [min, max], nên biến thể không thể vượt miền schema.
    invalid_base = copy.deepcopy(content)
    invalid_base["parameters"]["trigger_time"] = {"type": "number", "value": 5, "min": 0, "max": 500}
    invalid_base["events"][0]["trigger"] = {"type": "simulation_time", "time_s": {"$param": "trigger_time"}}
    invalid_path, _ = create_base(invalid_base)
    invalid_endpoint = invalid_path + "/versions/1/variant-batches"
    API.expect("GET", invalid_endpoint + "/" + batch["id"], 404)
    count_before, _ = API.expect("GET", "/api/v1/scenarios?pageSize=1", 200)
    for parameter_grid, tier in (({"trigger_time": [10, 120]}, "business"),):
        error, _ = API.expect("POST", invalid_endpoint, 422, {"parameterGrid": parameter_grid})
        check(error["code"] == "VARIANT_VALIDATION_FAILED" and error["variantIndex"] == 2, "second invalid combination rejects whole batch")
        check(error["report"]["stoppedAt"] == tier and error["report"]["errors"], "detailed " + tier + " report")
    count_after, _ = API.expect("GET", "/api/v1/scenarios?pageSize=1", 200)
    check(count_before["totalCount"] == count_after["totalCount"], "failed validation writes no scenarios")
    batches, _ = API.expect("GET", invalid_endpoint, 200)
    check(batches == [], "failed validation writes no batches")

    # Decimal tokens qua jsonb phải vẫn publish khớp hash; step là gợi ý, không ép lưới.
    decimal_batch, _ = generate(endpoint, {"parameterGrid": {"pedestrian_speed_ms": [1.25]}, "namePrefix": "x"})
    sid = decimal_batch["scenarios"][0]["id"]
    API.expect("POST", "/api/v1/scenarios/" + sid + "/versions", 201, {})
    check(True, "fractional value off suggested step publishes")

    with tempfile.TemporaryDirectory(prefix="asx-b12-") as temporary:
        folder = Path(temporary)
        grid_file = folder / "grid.json"
        grid_file.write_text(json.dumps({"parameterGrid": {"pedestrian_speed_ms": [1.25]}}))
        output = folder / "experiment"
        try:
            subprocess.run([sys.executable, str(ROOT / "tools/create_variant_experiment.py"),
                            "--scenario-id", base_path.rsplit("/", 1)[1], "--grid", str(grid_file),
                            "--output", str(output)], check=True, env=dict(os.environ, ASX_API_BASE=API.base))
        finally:
            if (output / "manifest.json").exists():
                manifest = json.loads((output / "manifest.json").read_text())
                CREATED.extend(s["id"] for s in manifest["scenarios"])
        check(len(manifest["versions"]) == 1, "experiment tool produces manifest")
        artifact = manifest["versions"][0]
        raw, _ = API.expect("GET", "/api/v1/scenarios/" + artifact["scenarioId"] + "/versions/1/export?format=json", 200, raw=True)
        check((output / artifact["file"]).read_text() == raw, "experiment files retain exact API numeric representation")
        published, _ = API.expect("GET", "/api/v1/scenarios/" + artifact["scenarioId"] + "/versions/1", 200)
        check(artifact["scenarioVersionId"] == published["summary"]["id"] and artifact["contentHash"] == published["summary"]["contentHash"], "manifest IDs and hashes match published versions")

    API.expect("POST", invalid_path + "/versions/1/deprecate", 200)
    error, _ = API.expect("POST", invalid_endpoint, 422, grid)
    check(error["code"] == "VARIANT_BASE_NOT_READY", "reject deprecated base")
    API.expect("POST", base_path + "/archive", 200)
    error, _ = API.expect("POST", endpoint, 422, grid)
    check(error["code"] == "SCENARIO_ARCHIVED", "reject archived base")
    API.expect("GET", endpoint + "/" + batch["id"], 200)
    check(True, "history remains readable after archival")


if __name__ == "__main__":
    try:
        run()
        print("%d checks passed" % CHECKS)
    finally:
        failures = []
        for sid in CREATED:
            status, response, _ = API.call("POST", "/api/v1/scenarios/" + sid + "/archive")
            if status != 200 and (not isinstance(response, dict) or response.get("code") != "SCENARIO_ARCHIVED"):
                failures.append(sid)
        print("Archived test scenarios: %d/%d (records remain for audit)" % (len(CREATED) - len(failures), len(CREATED)))
        if failures:
            raise RuntimeError("Could not archive: " + ", ".join(failures))
