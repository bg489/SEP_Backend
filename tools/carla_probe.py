"""Bounded CARLA inspection in an isolated process, never a simulator launcher.

The queue/acceptance parent must not retain native episode streaming sessions
while the recorder connects or the server restarts. A probe owns one Client,
returns plain JSON, closes it, and exits before another CARLA phase starts.
"""
import argparse
import gc
import json
import os
from pathlib import Path
import sys
import time

from carla_maps import normalize_map_name

MARKER = "ASX_CARLA_PROBE:"


def coordinates(transform):
    return {"x": transform.location.x, "y": transform.location.y, "z": transform.location.z,
            "yaw": (transform.rotation.yaw + 180) % 360 - 180}


def short_route(world):
    """Sample a straight drivable route and crossing actor; return no native objects."""
    world_map = world.get_map()
    points = world_map.get_spawn_points()
    for spawn in points:
        waypoint = world_map.get_waypoint(spawn.location)
        if waypoint is None or waypoint.is_junction:
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
            start, goal = coordinates(spawn), coordinates(path[-1].transform)
            goal["z"] += 0.2
            crossing = path[10].transform
            right = crossing.get_right_vector()
            actor = {"x": crossing.location.x - right.x * 4,
                     "y": crossing.location.y - right.y * 4,
                     "z": crossing.location.z + 0.7, "yaw": (crossing.rotation.yaw + 270) % 360 - 180}
            return {"start": start, "goal": goal, "actor": actor, "spawnPointCount": len(points)}
    raise RuntimeError("No straight 25m route was found; no random route is substituted")


def inspect_server(host, port, expected, timeout, fail_on_mismatch=False, route=False, client_factory=None):
    """Reuse exactly one Client and at most one World for a bounded observation."""
    if client_factory is None:
        sys.path.append(str(Path(os.environ.get("CARLA_ROOT", "")) / "PythonAPI" / "carla"))
        import carla
        client_factory = carla.Client
    expected = normalize_map_name(expected) if expected else None
    deadline = time.monotonic() + timeout
    client, world = client_factory(host, port, 1), None
    reason = "CARLA probe timed out"
    try:
        while time.monotonic() < deadline:
            try:
                client.set_timeout(max(0.1, min(3.0, deadline - time.monotonic())))
                version = client.get_server_version()
                if not expected and not route:
                    return {"ok": True, "version": version}
                if world is None:
                    world = client.get_world()
                actual = normalize_map_name(world.get_map().name)
                if expected and actual != expected:
                    reason = f"Expected map {expected}, server reports {actual}"
                    if fail_on_mismatch:
                        return {"ok": False, "map": actual, "reason": reason}
                else:
                    result = {"ok": True, "map": actual, "version": version}
                    if route:
                        result.update(short_route(world))
                    return result
            except Exception as error:
                reason = str(error)
            remaining = deadline - time.monotonic()
            if remaining > 0:
                time.sleep(min(0.5, remaining))
        return {"ok": False, "reason": reason}
    finally:
        # Native Client has no public close(). Release its sole episode before
        # this process exits; the parent waits for exit before recording/restart.
        world = None
        client = None
        gc.collect()
        time.sleep(0.2)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--timeout", type=float, required=True)
    parser.add_argument("--map", default="")
    parser.add_argument("--route", action="store_true")
    parser.add_argument("--fail-on-mismatch", action="store_true")
    args = parser.parse_args()
    if args.timeout <= 0 or not 1 <= args.port <= 65535:
        parser.error("Positive timeout and valid RPC port required")
    try:
        result = inspect_server(args.host, args.port, args.map, args.timeout, args.fail_on_mismatch, args.route)
    except Exception as error:
        result = {"ok": False, "reason": str(error)}
    print(MARKER + json.dumps(result), flush=True)
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
