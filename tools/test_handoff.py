#!/usr/bin/env python3
"""B13 integration tests: API bàn giao Flow 2 (danh sách READY + execution package).

ASX_API_BASE=http://localhost:5080 python3 tools/test_handoff.py
ASX_TOKEN dùng khi Auth:Mode=Supabase. Cần DB đã nạp map metadata (migration 104) và 103.
Tạo dữ liệu riêng, archive đúng các scenario của lần test trong finally; không sửa seed.
"""
import copy
import json
from pathlib import Path
import sys
import uuid

from variant_api import Api

ROOT = Path(__file__).resolve().parents[1]
API = Api()
CREATED = []
CHECKS = 0
READY = "/api/v1/scenario-versions/ready"


def check(condition, message):
    global CHECKS
    if not condition:
        raise AssertionError(message)
    CHECKS += 1
    print("PASS", message, flush=True)


def spawn_points(map_name):
    data = json.loads((ROOT / "map_metadata" / (map_name + ".json")).read_text(encoding="utf-8"))
    return {p["index"]: p for p in data["spawn_points"]}


def resolve(node, parameters):
    """Bản Python của ResolveParamReferences để đối chiếu resolvedContent."""
    if isinstance(node, dict):
        if set(node) == {"$param"}:
            return copy.deepcopy(parameters[node["$param"]]["value"])
        return {k: resolve(v, parameters) for k, v in node.items()}
    if isinstance(node, list):
        return [resolve(v, parameters) for v in node]
    return node


def contains_param(node):
    if isinstance(node, dict):
        return "$param" in node or any(contains_param(v) for v in node.values())
    if isinstance(node, list):
        return any(contains_param(v) for v in node)
    return False


def put_and_publish(path, content):
    draft, _ = API.expect("GET", path + "/draft", 200)
    API.expect("PUT", path + "/draft", 200, {"content": content}, {"If-Match": '"%s"' % draft["rowVersion"]})
    report, _ = API.expect("POST", path + "/draft/validate", 200)
    check(report["isValid"], "draft validates before publish (%s)" % content["map"]["name"])
    version, _ = API.expect("POST", path + "/versions", 201, {})
    return version


def create_published(content, label):
    scenario, _ = API.expect("POST", "/api/v1/scenarios", 201, {
        "name": "B13 %s %s" % (label, uuid.uuid4().hex[:8]), "category": "pedestrian"})
    CREATED.append(scenario["id"])
    path = "/api/v1/scenarios/" + scenario["id"]
    return scenario, path, put_and_publish(path, content)


def ready_for(scenario_id, **query):
    params = "&".join(["scenarioId=" + scenario_id] + ["%s=%s" % kv for kv in query.items()])
    page, _ = API.expect("GET", READY + "?" + params, 200)
    return page["items"]


def package(version_id, status=200):
    result, _ = API.expect("GET", "/api/v1/scenario-versions/%s/execution-package" % version_id, status)
    return result


def check_locations(pkg, map_name):
    points = spawn_points(map_name)
    content = pkg["resolvedContent"]
    expected = {"/route/start": content["route"]["start"]["spawn_point_index"],
                "/route/goal": content["route"]["goal"]["spawn_point_index"]}
    by_path = {loc["path"]: loc for loc in pkg["locations"]}
    for path, index in expected.items():
        loc = by_path.get(path)
        check(loc is not None and loc["spawnPointIndex"] == index, "%s %s index %s listed" % (map_name, path, index))
        p = points[index]
        t = loc["transform"]
        check(t is not None and [t["x"], t["y"], t["z"], t["yaw"]] == [p["x"], p["y"], p["z"], p["yaw"]],
              "%s %s transform equals map_metadata/%s.json[%s]" % (map_name, path, map_name, index))


def main():
    s23 = json.loads((ROOT / "examples/S23_pedestrian_crossing_rain.json").read_text(encoding="utf-8"))
    opt = json.loads((ROOT / "examples/S23_town05_opt_boundary.json").read_text(encoding="utf-8"))

    # 1. Danh sách READY và kiểm tra đầu vào
    page, _ = API.expect("GET", READY + "?pageSize=100", 200)
    check(page["totalCount"] >= 1 and all(i["contentHash"].startswith("sha256:") for i in page["items"]),
          "ready list returns versions with content hashes")
    check(all("content" not in i and "resolvedContent" not in i for i in page["items"]), "ready list omits content")
    API.expect("GET", READY + "?category=khong-co", 400)
    check(True, "invalid category returns 400")
    API.expect("GET", "/api/v1/scenario-versions/%s/execution-package" % uuid.uuid4(), 404)
    check(True, "unknown version id returns 404")

    # 2. Version mới xuất hiện trong view, gói chạy đã giải $param và spawn point
    scenario, path, v1 = create_published(s23, "base")
    sid, v1_id = scenario["id"], v1["summary"]["id"]
    items = ready_for(sid)
    check(len(items) == 1 and items[0]["scenarioVersionId"] == v1_id and items[0]["isLatestReady"],
          "published v1 appears in ready list as latest")
    check(items[0]["mapName"] == "Town05" and items[0]["scenarioCode"] == scenario["code"], "ready item carries map and code")

    pkg = package(v1_id)
    check(pkg["packageFormat"] == "asx.execution-package/1", "package format tag")
    check(pkg["runnable"] and pkg["issues"] == [], "v1 package runnable without issues")
    check(pkg["selectableForNewCampaign"] and pkg["versionStatus"] == "READY" and pkg["scenarioStatus"] == "ACTIVE",
          "v1 selectable for new campaign")
    check(pkg["contentHash"] == v1["summary"]["contentHash"], "package content hash equals published hash")
    check(pkg["map"]["name"] == "Town05" and pkg["map"]["carlaVersion"] == "0.9.16" and pkg["map"]["spawnPointCount"] == 302,
          "package map carries CARLA version and spawn point count")
    stored = v1["content"]
    check(not contains_param(pkg["resolvedContent"]), "resolved content has no $param left")
    check(pkg["resolvedContent"] == resolve(stored, stored["parameters"]),
          "resolved content equals stored content with parameters substituted")
    check(pkg["parameterValues"] == {k: v["value"] for k, v in stored["parameters"].items()},
          "parameterValues lists every parameter value")
    check_locations(pkg, "Town05")

    # 3. Nhiều version READY: cờ is_latest_ready và latestOnly
    API.expect("POST", path + "/draft", 201)
    changed = copy.deepcopy(s23)
    changed["parameters"]["trigger_distance_m"]["value"] = 20
    v2 = put_and_publish(path, changed)
    v2_id = v2["summary"]["id"]
    items = {i["versionNumber"]: i for i in ready_for(sid)}
    check(set(items) == {1, 2} and items[2]["isLatestReady"] and not items[1]["isLatestReady"],
          "v1 and v2 both READY, only v2 is latest")
    latest = ready_for(sid, latestOnly="true")
    check([i["versionNumber"] for i in latest] == [2], "latestOnly returns only v2")
    trigger = package(v2_id)["resolvedContent"]["events"][0]["trigger"]["distance_m"]
    check(trigger == 20, "v2 package resolves trigger distance to new parameter value 20")

    # 4. DEPRECATED: rời danh sách READY nhưng vẫn lấy được gói để chạy lại
    API.expect("POST", path + "/versions/1/deprecate", 200)
    check([i["versionNumber"] for i in ready_for(sid)] == [2], "deprecated v1 leaves ready list")
    old = package(v1_id)
    check(old["versionStatus"] == "DEPRECATED" and not old["selectableForNewCampaign"] and old["runnable"],
          "deprecated v1 package still served, not selectable")

    # 5. Bản _Opt: index giống nhau nhưng toạ độ lấy đúng của map _Opt
    _, _, v_opt = create_published(opt, "opt")
    opt_pkg = package(v_opt["summary"]["id"])
    check(opt_pkg["runnable"] and opt_pkg["map"]["name"] == "Town05_Opt", "Town05_Opt package runnable")
    check_locations(opt_pkg, "Town05_Opt")
    start = opt_pkg["locations"][0] if opt_pkg["locations"][0]["path"] == "/route/start" else opt_pkg["locations"][1]
    base_point = spawn_points("Town05")[start["spawnPointIndex"]]
    check([start["transform"]["x"], start["transform"]["y"]] != [base_point["x"], base_point["y"]],
          "Town05_Opt index %s differs from Town05 index %s" % (start["spawnPointIndex"], start["spawnPointIndex"]))

    # 6. Scenario ARCHIVED: rời danh sách READY, gói vẫn trả nhưng không chọn được
    API.expect("POST", path + "/archive", 200)
    check(ready_for(sid) == [], "archived scenario leaves ready list")
    archived = package(v2_id)
    check(archived["scenarioStatus"] == "ARCHIVED" and archived["versionStatus"] == "READY"
          and not archived["selectableForNewCampaign"], "archived scenario package not selectable")


if __name__ == "__main__":
    try:
        main()
        print("\n%d/%d checks passed" % (CHECKS, CHECKS))
    except Exception as error:  # noqa: BLE001
        print("\nFAIL after %d checks: %s" % (CHECKS, error), file=sys.stderr)
        sys.exit(1)
    finally:
        for scenario_id in CREATED:
            API.call("POST", "/api/v1/scenarios/%s/archive" % scenario_id)
        print("Archived %d test scenarios." % len(CREATED))
