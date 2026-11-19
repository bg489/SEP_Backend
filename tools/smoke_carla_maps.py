"""Run isolated, real CARLA map/recorder smoke checks without backend requests.

Uses its own RPC port (2010), original CARLA assets, Low quality and no video.
It never claims a campaign or uploads test telemetry. Short recordings are
TRUNCATED evidence, not PASS verdicts. The test restores the simulator INI files.
"""
import argparse
import datetime
import gc
import json
import math
import os
from pathlib import Path
import socket
import sys
from types import SimpleNamespace
import uuid

import campaign_worker as worker
from carla_maps import SUPPORTED_MAPS, discover_installed_maps, normalize_map_name


def transform(point):
    return {"x": point.location.x, "y": point.location.y, "z": point.location.z, "yaw": point.rotation.yaw}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--carla-root", default=worker.DEFAULT_CARLA_ROOT)
    parser.add_argument("--maps", nargs="+", default=list(SUPPORTED_MAPS))
    parser.add_argument("--port", type=int, default=2010)
    parser.add_argument("--boot-timeout", type=float, default=180)
    parser.add_argument("--out", default="recordings/map-smoke-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
    options = parser.parse_args()
    if not 1024 <= options.port <= 65535 or options.port == 2000:
        parser.error("Use a dedicated RPC port between 1024 and 65535, excluding the normal port 2000")
    with socket.socket() as probe:
        if probe.connect_ex(("127.0.0.1", options.port)) == 0:
            parser.error("The smoke-test port is already in use; no existing simulator will be stopped")
    installed = discover_installed_maps(options.carla_root)
    maps = [normalize_map_name(name) for name in options.maps]
    if any(name not in installed for name in maps):
        parser.error("Every requested map must be installed")
    os.environ["CARLA_ROOT"] = options.carla_root

    out = Path(options.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    config = Path(options.carla_root) / "CarlaUE4" / "Saved" / "Config" / "WindowsNoEditor"
    backups = {config / name: (config / name).read_bytes() if (config / name).exists() else None
               for name in ("GameUserSettings.ini", "Engine.ini")}
    # Persist a recovery copy before launching a native process, including when
    # the parent shell is forcibly closed instead of delivering KeyboardInterrupt.
    for path, data in backups.items():
        if data is not None:
            (out / (path.name + ".before")).write_bytes(data)
    args = SimpleNamespace(carla_root=options.carla_root, boot_map="", fixture=None,
                           host="127.0.0.1", port=options.port, windowed=False,
                           carla_boot_timeout=options.boot_timeout, tm_port=options.port + 20,
                           no_video=True, lite=False, max_sim_s=1.0, out=str(out))
    results = []
    try:
        for name in maps:
            print(f"\nREAL MAP SMOKE {name} ({len(results) + 1}/{len(maps)})", flush=True)
            try:
                worker.ensure_carla(args, boot_map=name)
                route = worker.probe_carla(args.host, args.port, 30, name, fail_on_mismatch=True, route=True)
                if not route.get("ok"):
                    raise RuntimeError(route.get("reason", "Isolated route probe failed"))
                actual = normalize_map_name(route["map"])
                if actual != name:
                    raise RuntimeError(f"Wrong map: expected {name}, got {actual}")
                content = {
                    "map": {"name": name}, "route": {"start": route["start"], "goal": route["goal"], "goal_tolerance_m": 5},
                    "weather": {"preset": "ClearNoon"}, "ego_vehicle": {"blueprint": "vehicle.tesla.model3", "role_name": "hero", "initial_speed_kmh": 10},
                    "actors": [], "events": [], "traffic": {},
                    "execution": {"seed": 42, "warmup_s": 0.1, "timeout_s": 10, "fixed_delta_seconds": 0.05},
                }
                package = {"runnable": True, "resolvedContent": content, "locations": [], "issues": [],
                           "map": {"name": name, "carlaVersion": "0.9.16"}, "scenarioVersionId": str(uuid.uuid4()),
                           "contentHash": "local-map-smoke-only", "scenarioCode": f"MAP-SMOKE-{name}", "versionNumber": 1}
                path, _ = worker.record_run(package, "normal", args)
                records = [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines()]
                ticks = [record for record in records if record["kind"] == "tick"]
                end = records[-1]
                if len(ticks) < 2 or end.get("outcome") not in ("TRUNCATED", "COMPLETED"):
                    raise RuntimeError(f"Recording did not produce normal ticks: {end}")
                first, last = ticks[0]["ego"], ticks[-1]["ego"]
                distance = math.dist((first["x"], first["y"]), (last["x"], last["y"]))
                if distance < 0.05:
                    raise RuntimeError("The Ego did not move during the real map smoke recording")
                entry = {"map": name, "actualMap": actual, "spawnPointCount": route["spawnPointCount"], "smokeOk": True,
                         "tickCount": len(ticks), "egoDistanceM": round(distance, 3), "outcome": end["outcome"], "telemetry": str(path)}
            except Exception as error:
                entry = {"map": name, "smokeOk": False, "error": str(error)}
            results.append(entry)
            (out / "report.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
            print(json.dumps(entry), flush=True)
    finally:
        worker.kill_carla(options.carla_root, options.port)
        for path, data in backups.items():
            if data is None:
                path.unlink(missing_ok=True)
            else:
                path.write_bytes(data)
    passed = sum(item["smokeOk"] for item in results)
    print(f"Real CARLA map smoke checks: {passed}/{len(maps)}. Report: {out / 'report.json'}")
    return 0 if passed == len(maps) else 1


if __name__ == "__main__":
    raise SystemExit(main())
