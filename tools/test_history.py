#!/usr/bin/env python3
"""Kiểm thử tích hợp timeline audit (GET /api/v1/scenarios/{id}/history) qua API thật.

ASX_API_BASE=http://localhost:5080 python3 tools/test_history.py
ASX_TOKEN dùng khi Auth:Mode=Supabase. Đi qua cả vòng đời một scenario rồi so thứ tự sự kiện,
số version, scenario liên quan (clone hai chiều, biến thể) và phân trang. Cuối lần chạy archive
các scenario đã tạo (dữ liệu và audit vẫn còn).
"""
import json
import urllib.request
import uuid
from pathlib import Path

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


def history(sid, query=""):
    page, _ = API.expect("GET", "/api/v1/scenarios/%s/history?pageSize=100%s" % (sid, query), 200)
    return page


def actions(items):
    return [(i["entityType"], i["action"]) for i in items]


def save_draft(path, content):
    draft, _ = API.expect("GET", path + "/draft", 200)
    API.expect("PUT", path + "/draft", 200, {"content": content}, {"If-Match": '"%s"' % draft["rowVersion"]})


def import_json(content, name):
    boundary = "----asx" + uuid.uuid4().hex
    body = b""
    for field, filename, data in (("file", "history.json", json.dumps(content).encode()), ("name", None, name.encode())):
        body += ("--%s\r\nContent-Disposition: form-data; name=\"%s\"%s\r\n" % (
            boundary, field, '; filename="%s"\r\nContent-Type: application/json' % filename if filename else "")).encode()
        body += b"\r\n" + data + b"\r\n"
    body += ("--%s--\r\n" % boundary).encode()
    request = urllib.request.Request(API.base + "/api/v1/scenarios/import", data=body, method="POST")
    request.add_header("Content-Type", "multipart/form-data; boundary=" + boundary)
    if API.token:
        request.add_header("Authorization", "Bearer " + API.token)
    with urllib.request.urlopen(request, timeout=120) as response:
        return json.loads(response.read().decode())


def run():
    content = json.loads((ROOT / "examples/S23_pedestrian_crossing_rain.json").read_text())
    scenario, _ = API.expect("POST", "/api/v1/scenarios", 201, {"name": "History test " + uuid.uuid4().hex[:6], "category": "pedestrian"})
    sid = scenario["id"]
    CREATED.append(sid)
    path = "/api/v1/scenarios/" + sid
    check(actions(history(sid)["items"]) == [("SCENARIO", "CREATE")], "new scenario starts with CREATE")

    # Sửa hồ sơ (migration 106): chỉ trường đổi được ghi; gửi lại đúng giá trị cũ thì không có sự kiện mới
    new_name = scenario["name"] + " (renamed)"
    API.expect("PATCH", path, 200, {"name": new_name, "category": "cut_in", "description": "History check"})
    API.expect("PATCH", path, 200, {"name": new_name, "category": "cut_in", "description": "History check"})
    profile = history(sid)["items"]
    check(actions(profile) == [("SCENARIO", "UPDATE_PROFILE"), ("SCENARIO", "CREATE")],
          "profile edit is recorded once, an unchanged PATCH adds nothing")
    check(profile[0]["details"]["changes"] == {
        "name": {"from": scenario["name"], "to": new_name},
        "category": {"from": "pedestrian", "to": "cut_in"},
        "description": {"from": None, "to": "History check"},
    }, "profile edit keeps old and new values of the changed fields")

    save_draft(path, content)
    content["execution"]["seed"] = 43
    save_draft(path, content)
    API.expect("POST", path + "/draft/validate", 200)
    API.expect("POST", path + "/versions", 201, {"changeNote": "first"})
    API.expect("GET", path + "/versions/1/export?format=json", 200, raw=True)
    API.expect("PUT", path + "/tags", 200, {"tags": ["hist-a"]})
    clone, _ = API.expect("POST", path + "/clone", 201, {"name": "History clone " + uuid.uuid4().hex[:6]})
    CREATED.append(clone["id"])
    batch, _ = API.expect("POST", path + "/versions/1/variant-batches", 201,
                          {"parameterGrid": {"npc_count": [0, 10]}, "namePrefix": "History variant"})
    CREATED.extend(s["id"] for s in batch["scenarios"])
    API.expect("POST", path + "/draft?fromVersion=1", 201)
    API.expect("GET", path + "/draft/export?format=yaml", 200, raw=True)
    API.expect("DELETE", path + "/draft", 204)
    API.expect("POST", path + "/versions/1/deprecate", 200)
    API.expect("POST", path + "/archive", 200)
    API.expect("POST", path + "/restore", 200)

    page = history(sid)
    items = page["items"]
    expected = [
        ("SCENARIO", "RESTORE"), ("SCENARIO", "ARCHIVE"), ("VERSION", "DEPRECATE"), ("DRAFT", "DISCARD_DRAFT"),
        ("DRAFT", "EXPORT"), ("DRAFT", "CREATE"), ("VARIANT_BATCH", "GENERATE_VARIANTS"), ("SCENARIO", "CLONE"),
        ("SCENARIO", "TAG"), ("VERSION", "EXPORT"), ("VERSION", "PUBLISH"), ("DRAFT", "VALIDATE"),
        ("DRAFT", "UPDATE_DRAFT"), ("DRAFT", "UPDATE_DRAFT"), ("SCENARIO", "UPDATE_PROFILE"), ("SCENARIO", "CREATE"),
    ]
    check(actions(items) == expected, "timeline lists the whole lifecycle newest first")
    check(page["totalCount"] == len(expected), "total count matches")
    by_action = {(i["entityType"], i["action"]): i for i in items}
    for key in (("VERSION", "DEPRECATE"), ("DRAFT", "CREATE"), ("VARIANT_BATCH", "GENERATE_VARIANTS"),
                ("SCENARIO", "CLONE"), ("VERSION", "EXPORT"), ("VERSION", "PUBLISH")):
        check(by_action[key]["versionNumber"] == 1, "%s %s refers to version 1" % key)
    outgoing = by_action[("SCENARIO", "CLONE")]
    check(outgoing["entityId"] == clone["id"] and outgoing["relatedScenario"]["code"] == clone["code"],
          "outgoing clone points to the new scenario")
    check(by_action[("SCENARIO", "TAG")]["details"]["tags"] == ["hist-a"], "details carry the added tags")
    check(by_action[("VARIANT_BATCH", "GENERATE_VARIANTS")]["details"]["generated_count"] == 2, "details carry the variant count")
    check(by_action[("DRAFT", "VALIDATE")]["details"]["is_valid"] is True, "details carry the validation result")
    check(all(i["actor"]["displayName"] for i in items), "every event names its actor")
    check(items == sorted(items, key=lambda i: (i["at"], i["id"]), reverse=True), "events are ordered newest first")

    clone_items = history(clone["id"])["items"]
    check(actions(clone_items) == [("SCENARIO", "CLONE")], "clone history starts with CLONE")
    check(clone_items[0]["relatedScenario"]["id"] == sid and clone_items[0]["versionNumber"] == 1,
          "incoming clone points to the source scenario and version")

    variant_items = history(batch["scenarios"][0]["id"])["items"]
    check(actions(variant_items) == [("SCENARIO", "CREATE")], "variant history starts with CREATE")
    check(variant_items[0]["relatedScenario"]["id"] == sid and variant_items[0]["versionNumber"] == 1,
          "variant points to its parent scenario and version")

    imported = import_json(content, "History import " + uuid.uuid4().hex[:6])
    CREATED.append(imported["scenario"]["id"])
    import_items = history(imported["scenario"]["id"])["items"]
    check(actions(import_items) == [("IMPORT", "IMPORT")] and import_items[0]["details"]["file"] == "history.json",
          "imported scenario history starts with IMPORT and the file name")

    first, _ = API.expect("GET", path + "/history?page=1&pageSize=6", 200)
    third, _ = API.expect("GET", path + "/history?page=3&pageSize=6", 200)
    check(len(first["items"]) == 6 and first["totalPages"] == 3 and len(third["items"]) == 4, "paging splits the timeline")
    check([i["id"] for i in first["items"] + third["items"]] == [i["id"] for i in items[:6] + items[12:]],
          "pages follow the same order")
    API.expect("GET", "/api/v1/scenarios/%s/history" % uuid.uuid4(), 404)
    check(True, "unknown scenario returns 404")


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
