#!/usr/bin/env python3
"""Sinh batch từ version READY, publish và xuất bộ JSON + manifest cho SV5.

python3 tools/create_variant_experiment.py --scenario-id <uuid> --version 1 --output /tmp/sv5-run
Mặc định dùng examples/experiments/SV5_pedestrian_grid.json trên bản gốc S23.
Lệnh này GIỮ các scenario READY trong DB. Output phải là thư mục mới.
"""
import argparse
import json
from pathlib import Path

from variant_api import Api


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario-id", required=True)
    parser.add_argument("--version", type=int, default=1)
    parser.add_argument("--grid", type=Path, default=root / "examples/experiments/SV5_pedestrian_grid.json")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    request = json.loads(args.grid.read_text(encoding="utf-8"))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    api = Api()
    endpoint = "/api/v1/scenarios/%s/versions/%d/variant-batches" % (args.scenario_id, args.version)
    result, _ = api.expect("POST", endpoint, 201, request)
    manifest = {"batch": result["batch"], "scenarios": result["scenarios"], "versions": []}

    def save_manifest():
        (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    # Ghi IDs ngay: nếu publish/export bị ngắt, người dùng vẫn tra được batch và phần đã hoàn thành.
    save_manifest()
    for scenario in result["scenarios"]:
        path = "/api/v1/scenarios/" + scenario["id"]
        version, _ = api.expect("POST", path + "/versions", 201, {"changeNote": "SV5 parameter sweep"})
        item = {"scenarioId": scenario["id"], "scenarioVersionId": version["summary"]["id"],
                "contentHash": version["summary"]["contentHash"],
                "parameterValues": {key: value["value"] for key, value in version["content"]["parameters"].items()},
                "seed": version["content"]["execution"]["seed"], "file": None}
        manifest["versions"].append(item)
        save_manifest()
        content, _ = api.expect("GET", path + "/versions/1/export?format=json", 200, raw=True)
        filename = scenario["code"] + "_v1.json"
        # Giữ nguyên cách biểu diễn số từ API (1.0 khác 1 trong quy tắc content_hash).
        (output / filename).write_text(content, encoding="utf-8")
        item["file"] = filename
        save_manifest()
    print("Batch %s: %d READY versions; manifest: %s" %
          (result["batch"]["id"], len(manifest["versions"]), output / "manifest.json"))


if __name__ == "__main__":
    main()
