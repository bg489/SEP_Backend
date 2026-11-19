#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Đối chiếu validator C# (qua API) với validator Python tham chiếu.

Chạy cùng một bộ file scenario qua hai bản và so kết quả. So trên
(valid, stopped_at, tập (tier, path)) chứ KHÔNG so nguyên văn thông điệp:
lỗi tầng 1 do thư viện JSON Schema sinh ra (JsonSchema.Net bên C#,
jsonschema bên Python) nên chữ nghĩa khác nhau là bình thường; lỗi tầng 2 và 3
do ta tự viết nên phải khớp từng chữ.

Cách dùng:
    export ASX_TOKEN="<access token>"          # bỏ qua nếu Auth:Mode = Dev
    python tools/compare_validators.py examples/*.json

Harness tự tạo một scenario tạm, lần lượt nạp từng file vào bản nháp, gọi
POST /draft/validate, rồi archive scenario đó ở cuối để không để lại rác.
"""
from __future__ import annotations

import json
import os
import ssl
import sys
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from validate_scenario import (  # noqa: E402
    DEFAULT_SCHEMA,
    load_document,
    load_map_metadata,
    validate,
)

API_BASE = os.environ.get("ASX_API_BASE", "https://localhost:7080")
TOKEN = os.environ.get("ASX_TOKEN", "")

# Chứng chỉ dev tự ký
SSL_CONTEXT = ssl.create_default_context()
SSL_CONTEXT.check_hostname = False
SSL_CONTEXT.verify_mode = ssl.CERT_NONE


def call(method, path, body=None, headers=None):
    """Gọi API, trả (status, body đã parse, headers)."""
    url = API_BASE + path
    data = json.dumps(body).encode("utf-8") if body is not None else None
    request = urllib.request.Request(url, data=data, method=method)
    request.add_header("Accept", "application/json")
    if data is not None:
        request.add_header("Content-Type", "application/json")
    if TOKEN:
        request.add_header("Authorization", "Bearer " + TOKEN)
    for key, value in (headers or {}).items():
        request.add_header(key, value)

    try:
        with urllib.request.urlopen(request, context=SSL_CONTEXT, timeout=30) as response:
            text = response.read().decode("utf-8")
            parsed = json.loads(text) if text.strip() else None
            return response.status, parsed, dict(response.headers)
    except urllib.error.HTTPError as err:
        text = err.read().decode("utf-8")
        try:
            parsed = json.loads(text) if text.strip() else None
        except ValueError:
            parsed = text
        return err.code, parsed, dict(err.headers)


def fail(message):
    sys.stderr.write(message + "\n")
    raise SystemExit(2)


def csharp_reports(paths):
    """Chạy từng file qua API C#, trả {path: report}."""
    status, scenario, _ = call("POST", "/api/v1/scenarios", {
        "name": "TAM - doi chieu validator C# va Python",
        "description": "Scenario tạm do tools/compare_validators.py tạo; sẽ được archive ở cuối.",
        "category": "other",
    })
    if status != 201:
        fail("Không tạo được scenario tạm (HTTP %s): %s" % (status, scenario))
    scenario_id = scenario["id"]
    print("Scenario tạm: %s (%s)" % (scenario["code"], scenario_id))

    reports = {}
    try:
        for path in paths:
            content = load_document(path)

            status, draft, headers = call("GET", "/api/v1/scenarios/%s/draft" % scenario_id)
            if status != 200:
                fail("GET draft hỏng (HTTP %s): %s" % (status, draft))
            etag = headers.get("ETag") or '"%s"' % draft["rowVersion"]

            status, draft, _ = call(
                "PUT", "/api/v1/scenarios/%s/draft" % scenario_id,
                {"content": content}, {"If-Match": etag})
            if status != 200:
                fail("PUT draft hỏng cho %s (HTTP %s): %s" % (path, status, draft))

            status, report, _ = call(
                "POST", "/api/v1/scenarios/%s/draft/validate" % scenario_id)
            if status != 200:
                fail("POST validate hỏng cho %s (HTTP %s): %s" % (path, status, report))
            reports[path] = report
    finally:
        call("POST", "/api/v1/scenarios/%s/archive" % scenario_id)
        print("Đã archive scenario tạm.\n")

    return reports


def key_set(items):
    """Tập (tier, path) để so sánh, bỏ qua nguyên văn thông điệp."""
    return {(i.get("tier"), i.get("path")) for i in items or []}


def message_map(items):
    return {(i.get("tier"), i.get("path")): i.get("message") for i in items or []}


def compare(path, py, cs):
    """So một cặp report. Trả danh sách khác biệt (rỗng = khớp)."""
    diffs = []

    cs_valid = cs.get("isValid", cs.get("valid"))
    cs_stopped = cs.get("stoppedAt", cs.get("stopped_at"))
    if bool(py["valid"]) != bool(cs_valid):
        diffs.append("valid: Python=%s, C#=%s" % (py["valid"], cs_valid))
    if (py["stopped_at"] or None) != (cs_stopped or None):
        diffs.append("stopped_at: Python=%s, C#=%s" % (py["stopped_at"], cs_stopped))

    for label, py_items, cs_items in (
        ("errors", py["errors"], cs.get("errors")),
        ("warnings", py["warnings"], cs.get("warnings")),
    ):
        py_keys, cs_keys = key_set(py_items), key_set(cs_items)
        for missing in sorted(py_keys - cs_keys):
            diffs.append("%s chỉ Python có: %s %s" % (label, missing[0], missing[1]))
        for extra in sorted(cs_keys - py_keys):
            diffs.append("%s chỉ C# có   : %s %s" % (label, extra[0], extra[1]))

        # Thông điệp tầng 2 và 3 do ta tự viết nên phải khớp từng chữ
        py_msg, cs_msg = message_map(py_items), message_map(cs_items)
        for key in sorted(py_keys & cs_keys):
            if key[0] == "schema":
                continue
            if py_msg[key] != cs_msg[key]:
                diffs.append(
                    "%s lệch thông điệp ở %s %s\n      Python: %s\n      C#    : %s"
                    % (label, key[0], key[1], py_msg[key], cs_msg[key]))
    return diffs


def main(argv):
    paths = argv[1:]
    if not paths:
        fail("Dùng: python tools/compare_validators.py <file.json> [...]")

    with open(DEFAULT_SCHEMA, "r", encoding="utf-8") as handle:
        schema = json.load(handle)
    maps = load_map_metadata(os.environ.get("ASX_METADATA_DIR"))

    cs_all = csharp_reports(paths)

    total_diffs = 0
    for path in paths:
        py = validate(load_document(path), schema, maps, bool(maps))
        cs = cs_all[path]
        diffs = compare(path, py, cs)
        name = os.path.basename(path)
        if diffs:
            total_diffs += len(diffs)
            print("LỆCH  %s" % name)
            for diff in diffs:
                print("    - %s" % diff)
        else:
            state = "hợp lệ" if py["valid"] else "dừng ở tầng %s" % py["stopped_at"]
            print("KHỚP  %-38s %s, %d lỗi, %d cảnh báo"
                  % (name, state, len(py["errors"]), len(py["warnings"])))

    print("\n%d file, %d khác biệt" % (len(paths), total_diffs))
    return 0 if total_diffs == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
