#!/usr/bin/env python3
"""Kiểm thử tích hợp tag mức scenario qua API thật.

ASX_API_BASE=http://localhost:5080 python3 tools/test_tags.py
ASX_TOKEN dùng khi Auth:Mode=Supabase. Tag dùng tiền tố ngẫu nhiên để không đụng dữ liệu có sẵn;
cuối lần chạy gỡ tag rồi archive các scenario đã tạo (dữ liệu vẫn còn để audit).
"""
import json
import uuid
from pathlib import Path
import urllib.request

from variant_api import Api

ROOT = Path(__file__).resolve().parents[1]
API = Api()
CREATED = []
CHECKS = 0
P = "t" + uuid.uuid4().hex[:6]          # tiền tố tag của lần chạy, vd t3fa9c1


def check(condition, message):
    global CHECKS
    if not condition:
        raise AssertionError(message)
    CHECKS += 1
    print("PASS", message, flush=True)


def tag(name):
    return P + "-" + name


def create_ready(content, name="Tags test"):
    scenario, _ = API.expect("POST", "/api/v1/scenarios", 201, {
        "name": name + " " + uuid.uuid4().hex[:6], "category": "pedestrian"})
    sid = scenario["id"]
    CREATED.append(sid)
    path = "/api/v1/scenarios/" + sid
    draft, _ = API.expect("GET", path + "/draft", 200)
    API.expect("PUT", path + "/draft", 200, {"content": content}, {"If-Match": '"%s"' % draft["rowVersion"]})
    API.expect("POST", path + "/draft/validate", 200)
    API.expect("POST", path + "/versions", 201, {})
    return sid, path


def import_json(content, name):
    boundary = "----asx" + uuid.uuid4().hex
    parts = [
        ("file", "tags.json", "application/json", json.dumps(content).encode()),
        ("name", None, None, name.encode()),
    ]
    body = b""
    for field, filename, ctype, data in parts:
        body += ("--%s\r\n" % boundary).encode()
        disposition = 'form-data; name="%s"' % field + ('; filename="%s"' % filename if filename else "")
        body += ("Content-Disposition: %s\r\n" % disposition).encode()
        if ctype:
            body += ("Content-Type: %s\r\n" % ctype).encode()
        body += b"\r\n" + data + b"\r\n"
    body += ("--%s--\r\n" % boundary).encode()
    request = urllib.request.Request(API.base + "/api/v1/scenarios/import", data=body, method="POST")
    request.add_header("Content-Type", "multipart/form-data; boundary=" + boundary)
    request.add_header("Accept", "application/json")
    if API.token:
        request.add_header("Authorization", "Bearer " + API.token)
    with urllib.request.urlopen(request, timeout=120) as response:
        result = json.loads(response.read().decode())
        check(response.status == 201, "import returns 201")
    CREATED.append(result["scenario"]["id"])
    return result


def listed(query):
    page, _ = API.expect("GET", "/api/v1/scenarios?pageSize=100&" + query, 200)
    return {item["id"] for item in page["items"]}


def usage():
    tags, _ = API.expect("GET", "/api/v1/tags", 200)
    return {t["name"]: t for t in tags}


def put_tags(path, tags, status=200):
    result, _ = API.expect("PUT", path + "/tags", status, {"tags": tags})
    return result


def run():
    content = json.loads((ROOT / "examples/S23_pedestrian_crossing_rain.json").read_text())
    sid, path = create_ready(content)

    detail, _ = API.expect("GET", path, 200)
    check(detail["tags"] == [], "new scenario has no tags")
    result = put_tags(path, [" " + tag("Rain").upper() + " ", tag("urban"), tag("rain")])
    check(result["tags"] == sorted([tag("rain"), tag("urban")]), "tags are normalized, deduplicated and sorted")
    detail, _ = API.expect("GET", path, 200)
    check(detail["tags"] == result["tags"], "detail returns scenario tags")
    row = next(i for i in API.expect("GET", "/api/v1/scenarios?pageSize=100&tag=" + tag("rain"), 200)[0]["items"] if i["id"] == sid)
    check(row["tags"] == result["tags"], "repository row returns tags")
    check(listed("tag=" + tag("rain")) == {sid}, "filter by one tag")
    check(listed("tag=%s&tag=%s" % (tag("rain"), tag("urban"))) == {sid}, "filter by several tags requires all")
    check(listed("tag=%s&tag=%s" % (tag("rain"), tag("none"))) == set(), "missing tag in filter returns nothing")
    check(listed("tag=" + tag("RAIN")) == {sid}, "filter tag is normalized")
    API.expect("GET", "/api/v1/scenarios?tag=bad%20tag", 400)
    check(True, "invalid filter tag is rejected")

    counts = usage()
    check(counts[tag("rain")]["scenarioCount"] == 1 and counts[tag("rain")]["activeScenarioCount"] == 1, "tag usage counts scenarios")

    check(put_tags(path, [tag("urban"), tag("rain")])["tags"] == result["tags"], "same set is accepted without changes")
    for invalid, field in (([tag("ok"), "bad tag"], "tags[1]"), ([None], "tags[0]"), ([""], "tags[0]"),
                           (["-x"], "tags[0]"), ([tag(str(i)) for i in range(21)], "tags")):
        error = put_tags(path, invalid, 400)
        check(field in error.get("errors", {}), "reject invalid tags at " + field)
    error, _ = API.expect("PUT", path + "/tags", 400, {})
    check("tags" in error.get("errors", {}), "reject missing tag list")
    API.expect("PUT", "/api/v1/scenarios/%s/tags" % uuid.uuid4(), 404, {"tags": []})
    check(True, "unknown scenario returns 404")

    # Clone và biến thể kế thừa tag của scenario nguồn.
    clone, _ = API.expect("POST", path + "/clone", 201, {"name": "Tags clone " + P})
    CREATED.append(clone["id"])
    check(clone["tags"] == result["tags"], "clone response inherits tags")
    check(listed("tag=" + tag("rain")) == {sid, clone["id"]}, "clone is found by inherited tag")
    batch, _ = API.expect("POST", path + "/versions/1/variant-batches", 201,
                          {"parameterGrid": {"npc_count": [0, 10]}, "namePrefix": "Tags variant " + P})
    variant_ids = [s["id"] for s in batch["scenarios"]]
    CREATED.extend(variant_ids)
    for vid in variant_ids:
        variant, _ = API.expect("GET", "/api/v1/scenarios/" + vid, 200)
        check(variant["tags"] == result["tags"], "variant inherits tags")
    check(usage()[tag("rain")]["scenarioCount"] == 4, "usage counts clone and variants")

    # Import: metadata.tags của file thành tag mức scenario, nội dung draft giữ nguyên.
    imported_content = json.loads(json.dumps(content))
    imported_content["metadata"]["tags"] = [tag("imported"), tag("rain")]
    imported = import_json(imported_content, "Tags import " + P)
    check(imported["scenario"]["tags"] == sorted([tag("imported"), tag("rain")]), "import uses metadata.tags as scenario tags")
    draft, _ = API.expect("GET", "/api/v1/scenarios/%s/draft" % imported["scenario"]["id"], 200)
    check(draft["content"]["metadata"]["tags"] == imported_content["metadata"]["tags"], "imported draft keeps metadata.tags")

    # Gỡ một tag, rồi scenario lưu trữ không đổi tag được nhưng vẫn lọc được trong tab Archive.
    check(put_tags(path, [tag("rain")])["tags"] == [tag("rain")], "removing a tag")
    check(sid not in listed("tag=" + tag("urban")), "removed tag no longer matches")
    API.expect("POST", path + "/archive", 200)
    error = put_tags(path, [tag("x")], 422)
    check(error["code"] == "SCENARIO_ARCHIVED", "archived scenario tags are read-only")
    check(listed("status=ARCHIVED&tag=" + tag("rain")) == {sid}, "archived scenarios filter by tag")
    counts = usage()
    check(counts[tag("rain")]["activeScenarioCount"] == counts[tag("rain")]["scenarioCount"] - 1, "usage separates archived scenarios")
    API.expect("POST", path + "/restore", 200)


if __name__ == "__main__":
    try:
        run()
        print("%d checks passed" % CHECKS)
    finally:
        failures = []
        for sid in CREATED:
            path = "/api/v1/scenarios/" + sid
            API.call("POST", path + "/restore")          # gỡ tag cần scenario ACTIVE
            status, _, _ = API.call("PUT", path + "/tags", {"tags": []})
            archived, response, _ = API.call("POST", path + "/archive")
            if status != 200 or (archived != 200 and (not isinstance(response, dict) or response.get("code") != "SCENARIO_ARCHIVED")):
                failures.append(sid)
        left = [n for n in usage() if n.startswith(P + "-")]
        print("Cleaned test scenarios: %d/%d; test tags still in use: %d" % (len(CREATED) - len(failures), len(CREATED), len(left)))
        if failures or left:
            raise RuntimeError("Cleanup incomplete: %s %s" % (failures, left))
