"""Real API -> database -> CARLA -> Oracle -> video acceptance, one complete short route per map.

Uses the actual campaign_worker/recorder and public HTTP API. No fixture telemetry, --quick,
--simulate or max_sim_s cap. Run only against a disposable/staging database configured by the team.
Example: py -3.12 tools/review2_live_acceptance.py --api http://localhost:5080 --maps Town01 Town01_Opt
Set ASX_ACCESS_TOKEN for Supabase; explicit development mode uses X-Asx-Dev-Role.
"""
import argparse
from datetime import datetime, timezone
import gc
import json
import math
import os
from pathlib import Path
import sys
import threading
from types import SimpleNamespace
import urllib.error
import urllib.request
import uuid

import campaign_worker as worker
from carla_maps import SUPPORTED_MAPS, normalize_map_name


def api(base, method, path, data=None, role="ADMIN", extra_headers=None):
    token = os.environ.get("ASX_ACCESS_TOKEN")
    headers = {"Authorization": f"Bearer {token}"} if token else {"X-Asx-Dev-Role": role}
    if data is not None:
        headers["Content-Type"] = "application/json"
    headers.update(extra_headers or {})
    request = urllib.request.Request(base.rstrip("/") + path, method=method, headers=headers,
                                    data=None if data is None else json.dumps(data).encode("utf-8"))
    try:
        with urllib.request.urlopen(request, timeout=180) as response:
            raw = response.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"{method} {path}: HTTP {error.code} {error.read().decode('utf-8', errors='replace')}") from error


def coordinates(transform):
    return {"x": transform.location.x, "y": transform.location.y, "z": transform.location.z,
            "yaw": (transform.rotation.yaw + 180) % 360 - 180}


def short_route(world, carla):
    """Choose a known spawn then follow its own driving lane, avoiding cross-lane teleportation."""
    world_map = world.get_map()
    for spawn in world_map.get_spawn_points():
        waypoint = world_map.get_waypoint(spawn.location)
        if waypoint.is_junction:
            continue
        path = [waypoint]
        for _ in range(30):
            following = path[-1].next(1.0)
            if not following:
                break
            next_point = following[0]
            if next_point.is_junction or abs(next_point.transform.rotation.pitch) > 5:
                break
            path.append(next_point)
        if len(path) >= 25 and path[0].transform.location.distance(path[-1].transform.location) >= 20:
            start = coordinates(spawn)
            goal = coordinates(path[-1].transform)
            goal["z"] += 0.2
            crossing = path[10].transform
            right = crossing.get_right_vector()
            actor = {"x": crossing.location.x - right.x * 4,
                     "y": crossing.location.y - right.y * 4,
                     "z": crossing.location.z + 0.7, "yaw": (crossing.rotation.yaw + 270) % 360 - 180}
            return start, goal, actor
    raise RuntimeError("No straight 25m route was found; no random route is substituted")


def create_published_scenario(base, map_name, start, goal, actor, label):
    content = {
        "schema_version": "1.0", "metadata": {"name": label, "category": "pedestrian"},
        "map": {"name": map_name}, "weather": {"preset": "ClearNoon"},
        "route": {"start": start, "goal": goal, "speed_limit_kmh": 20, "goal_tolerance_m": 2},
        "ego_vehicle": {"blueprint": "vehicle.tesla.model3", "role_name": "hero", "initial_speed_kmh": 0},
        "actors": [{"id": "review2_crossing", "type": "pedestrian", "blueprint": "walker.pedestrian.0001",
                    "spawn": actor, "behavior": "idle", "initial_speed_kmh": 0}],
        "events": [{"id": "crossing_event", "type": "pedestrian_crossing", "actor_ref": "review2_crossing",
                    "trigger": {"type": "simulation_time", "time_s": 1},
                    "params": {"speed_ms": 1.4, "direction": "left_to_right"}}],
        "execution": {"seed": 42, "warmup_s": 0.5, "timeout_s": 60,
                      "synchronous_mode": True, "fixed_delta_seconds": 0.05},
        "traffic": {"npc_vehicles": {"count": 0}, "pedestrians": {"count": 0}},
        "source": {"type": "manual"}, "parameters": {},
    }
    scenario = api(base, "POST", "/api/v1/scenarios", {"name": label, "category": "pedestrian",
        "description": "Real Review 2 acceptance on an entire short lane route with an observed pedestrian event."})
    scenario_id = scenario["id"]
    draft = api(base, "GET", f"/api/v1/scenarios/{scenario_id}/draft")
    api(base, "PUT", f"/api/v1/scenarios/{scenario_id}/draft", {"content": content},
        extra_headers={"If-Match": f'"{draft["rowVersion"]}"'})
    validation = api(base, "POST", f"/api/v1/scenarios/{scenario_id}/draft/validate")
    if not validation["isValid"]:
        raise RuntimeError("Scenario validation failed: " + json.dumps(validation["errors"]))
    published = api(base, "POST", f"/api/v1/scenarios/{scenario_id}/versions", {"changeNote": "Real acceptance route, no quick/simulated telemetry."})
    return scenario_id, published["summary"]["id"], content


def inspect_recording(path):
    records = [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines()]
    ticks = [row for row in records if row["kind"] == "tick"]
    if len(ticks) < 2:
        raise RuntimeError("Real recording has fewer than two ticks")
    first, last = ticks[0]["ego"], ticks[-1]["ego"]
    distance = math.dist((first["x"], first["y"]), (last["x"], last["y"]))
    actions = [row for row in records if row.get("type") == "scenario_action"]
    actor_id = records[0].get("scripted_actors", {}).get("review2_crossing")
    actor_positions = [actor for tick in ticks for actor in tick.get("actors", []) if actor["id"] == actor_id]
    actor_motion = 0 if len(actor_positions) < 2 else math.dist(
        (actor_positions[0]["x"], actor_positions[0]["y"]), (actor_positions[-1]["x"], actor_positions[-1]["y"]))
    return {"outcome": records[-1].get("outcome"), "tickCount": len(ticks), "egoDistanceM": round(distance, 3),
            "actorDistanceM": round(actor_motion, 3), "executedEventIds": [row["event_id"] for row in actions],
            "sut": records[0].get("sut"), "fullRoute": records[-1].get("outcome") == "COMPLETED",
            "telemetry": str(path)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://localhost:5080")
    parser.add_argument("--carla-root", default=worker.DEFAULT_CARLA_ROOT)
    parser.add_argument("--maps", nargs="+", default=list(SUPPORTED_MAPS))
    parser.add_argument("--port", type=int, default=2000)
    parser.add_argument("--sut", choices=("behavior_agent", "autopilot"), default="behavior_agent")
    parser.add_argument("--out", default="recordings/review2-live-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
    parser.add_argument("--no-video", action="store_true", help="Diagnostic only; acceptance will remain incomplete without video")
    arguments = parser.parse_args()
    os.environ["CARLA_ROOT"] = arguments.carla_root
    sys.path.append(str(Path(arguments.carla_root) / "PythonAPI" / "carla"))
    import carla
    out = Path(arguments.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    args = SimpleNamespace(api=arguments.api, carla_root=arguments.carla_root, host="127.0.0.1", port=arguments.port,
        tm_port=arguments.port + 6000, carla_boot_timeout=180, windowed=not arguments.no_video,
        lite=False, no_video=arguments.no_video, max_sim_s=None, out=str(out), boot_map="", fixture=None,
        simulate=False, profile="normal", max_infra_retries=0, publish_progress=True,
        worker_name="review2-live-" + uuid.uuid4().hex[:8])
    original_role = os.environ.get("ASX_DEV_ROLE")
    os.environ["ASX_DEV_ROLE"] = "ADMIN"
    # Recover expired leases before a new process starts sending heartbeats.
    api(arguments.api, "POST", "/api/v1/workers/recover-lost")
    worker_id = worker.register_worker(arguments.api, args)
    stop = threading.Event()
    def beat():
        while not stop.wait(20):
            try:
                worker.heartbeat(arguments.api, worker_id, args)
            except Exception as error:
                print(f"heartbeat: {error}", flush=True)
    worker.heartbeat(arguments.api, worker_id, args)
    threading.Thread(target=beat, daemon=True).start()
    evidence = []
    try:
        for map_name in (normalize_map_name(name) for name in arguments.maps):
            entry = {"map": map_name, "sutType": arguments.sut, "startedAt": datetime.now(timezone.utc).isoformat()}
            print(f"REAL FULL ROUTE {map_name} ({len(evidence)+1}/{len(arguments.maps)})", flush=True)
            try:
                worker.ensure_carla(args, boot_map=map_name)
                client = carla.Client(args.host, args.port)
                client.set_timeout(60)
                world = client.get_world()
                start, goal, actor = short_route(world, carla)
                del world, client
                gc.collect()
                label = f"Review2 {map_name} {uuid.uuid4().hex[:6]}"
                scenario_id, version_id, content = create_published_scenario(arguments.api, map_name, start, goal, actor, label)
                entry.update(scenarioId=scenario_id, versionId=version_id)
                (out / f"{map_name}-scenario.json").write_text(json.dumps(content, indent=2), encoding="utf-8")
                campaign = api(arguments.api, "POST", "/api/v1/campaigns", {"name": label, "scenarioVersionIds": [version_id],
                    "sutType": arguments.sut, "sutBehavior": "normal", "sutCarlaVersion": "0.9.16"})
                api(arguments.api, "POST", f'/api/v1/campaigns/{campaign["id"]}/start')
                entry["campaignId"] = campaign["id"]
                claimed = worker.claim(arguments.api, campaign["id"], worker_id)
                if not claimed:
                    raise RuntimeError("New campaign had no claimable run for the registered healthy worker")
                claimed["maxInfraRetries"] = 0
                entry["runId"] = claimed["runId"]
                result = worker.run_one(arguments.api, claimed, args)
                entry["apiResult"] = result
                matches = list(out.glob(f'*{claimed["runId"][:8]}.jsonl'))
                if not matches:
                    raise RuntimeError("No actual recorder telemetry file was produced")
                observed = inspect_recording(matches[0])
                entry.update(observed)
                entry["accepted"] = bool(observed["fullRoute"] and observed["egoDistanceM"] >= 15
                    and observed["actorDistanceM"] >= 0.5 and "crossing_event" in observed["executedEventIds"]
                    and result.get("hasVideo") and result.get("verdict") in ("PASS", "FAIL"))
                # Fetch again: video is uploaded after the telemetry result response.
                entry["apiResult"] = api(arguments.api, "GET", f'/api/v1/runs/{claimed["runId"]}/result')
                entry["accepted"] = bool(observed["fullRoute"] and observed["egoDistanceM"] >= 15
                    and observed["actorDistanceM"] >= 0.5 and "crossing_event" in observed["executedEventIds"]
                    and entry["apiResult"].get("hasVideo") and entry["apiResult"].get("verdict") in ("PASS", "FAIL"))
            except Exception as error:
                entry["accepted"] = False
                entry["error"] = str(error)
            entry["finishedAt"] = datetime.now(timezone.utc).isoformat()
            evidence.append(entry)
            (out / "report.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
            print(json.dumps(entry, ensure_ascii=True), flush=True)
    finally:
        stop.set()
        worker._restore_packaged_boot_map(args)
        if original_role is None:
            os.environ.pop("ASX_DEV_ROLE", None)
        else:
            os.environ["ASX_DEV_ROLE"] = original_role
    passed = sum(entry["accepted"] for entry in evidence)
    print(f"Actual full-route acceptance: {passed}/{len(evidence)}; {out / 'report.json'}", flush=True)
    return 0 if passed == len(evidence) else 1


if __name__ == "__main__":
    raise SystemExit(main())
