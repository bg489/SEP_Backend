"""Execute saved actor fields with CARLA 0.9.16, with measured native oracles.

Route/trigger/action descriptions are documentation, not executable commands.
"""

import argparse
from collections import deque
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import random
import signal
import sys
import threading
import time
import uuid

import carla


FIXED_DELTA_SECONDS = 0.05
SUPPORTED_RULES = {"collision", "speed", "speeding", "freezing", "stagnation"}


def save_manifest(path, manifest):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    temporary.replace(path)


def critical_settings(settings):
    return {name: getattr(settings, name) for name in (
        "synchronous_mode", "fixed_delta_seconds", "no_rendering_mode",
        "substepping", "max_substep_delta_time", "max_substeps",
    )}


def cleanup(config_path, host, port):
    """Recover only recorded actors from the same CARLA episode after hard kill."""
    path = Path(config_path).with_name("cleanup-manifest.json")
    errors = []
    if not path.exists():
        print("CARLA_CLEANUP " + json.dumps({"complete": True, "reason": "No owned resource manifest exists."}), flush=True)
        return 0
    try:
        manifest = json.loads(path.read_text(encoding="utf-8-sig"))
        if manifest.get("complete"):
            print("CARLA_CLEANUP " + json.dumps({"complete": True, "reason": "Resources were already released."}), flush=True)
            return 0
        if manifest.get("formatVersion") != 1 or manifest.get("port") != port or manifest.get("host") != host:
            raise RuntimeError("The cleanup manifest does not match this CARLA connection.")
        client = carla.Client(host, port)
        client.set_timeout(5.0)
        world = client.get_world()
        if world.id != manifest.get("worldId") or world.get_map().name != manifest.get("map"):
            # Actor IDs may have been reused by a new world. Never touch them.
            manifest["complete"] = True
            manifest["completionReason"] = "The original CARLA episode no longer exists; no resources were touched."
            save_manifest(path, manifest)
            print("CARLA_CLEANUP " + json.dumps({"complete": True, "reason": manifest["completionReason"]}), flush=True)
            return 0
        for entry in reversed(manifest.get("actors", [])):
            if entry.get("released"):
                continue
            try:
                actor = world.get_actor(entry["id"])
                if actor is None or not actor.is_alive:
                    entry["released"] = True
                elif (actor.type_id != entry["blueprint"] or
                      actor.attributes.get("role_name") != entry.get("role") or
                      (actor.parent.id if actor.parent else None) != entry.get("parentId")):
                    # A nonmatching actor is not ours, even when its integer ID matches.
                    entry["released"] = True
                    entry["skipped"] = "Actor identity no longer matches the owned resource."
                else:
                    if entry["kind"] in {"sensor", "controller"}:
                        try:
                            if entry["kind"] == "controller" or actor.is_listening:
                                actor.stop()
                        except RuntimeError:
                            pass
                    response = client.apply_batch_sync([carla.command.DestroyActor(actor.id)], False)[0]
                    if not response.has_error():
                        entry["released"] = True
                    else:
                        raise RuntimeError(f"CARLA did not destroy owned actor {actor.id}: {response.error}")
                save_manifest(path, manifest)
            except (RuntimeError, OSError) as exception:
                errors.append(str(exception))
        try:
            previous = manifest["originalSettings"]
            manager = client.get_trafficmanager(manifest["trafficManagerPort"])
            manager.set_synchronous_mode(bool(previous["synchronous_mode"]))
            settings = world.get_settings()
            for name, setting in previous.items():
                setattr(settings, name, setting)
            world.apply_settings(settings)
            manifest["settingsRestored"] = True
        except RuntimeError as exception:
            errors.append(str(exception))
        manifest["complete"] = not errors and all(entry.get("released") for entry in manifest.get("actors", []))
        manifest["cleanupErrors"] = errors
        save_manifest(path, manifest)
    except (OSError, ValueError, RuntimeError, KeyError, TypeError) as exception:
        errors.append(str(exception))
    print("CARLA_CLEANUP " + json.dumps({"complete": not errors, "errors": errors}), flush=True)
    return 0 if not errors else 2


def value(config, key, fallback=None):
    result = config.get(key, config.get(key[0].upper() + key[1:], fallback))
    return fallback if result is None else result


def integer(config, key, fallback, minimum, maximum):
    result = int(value(config, key, fallback))
    if not minimum <= result <= maximum:
        raise ValueError(f"{key} must be between {minimum} and {maximum}.")
    return result


def weather_for(name):
    # The packaged client has ClearSunset; ClearMorning and Sunset do not exist.
    presets = {
        "Clear noon": carla.WeatherParameters.ClearNoon,
        "Sunset": carla.WeatherParameters.ClearSunset,
        "Overcast": carla.WeatherParameters.CloudyNoon,
        "Light rain": carla.WeatherParameters.SoftRainNoon,
        "Heavy rain": carla.WeatherParameters.HardRainNoon,
    }
    if name == "Clear morning":
        return carla.WeatherParameters(sun_altitude_angle=15.0, sun_azimuth_angle=90.0)
    if name == "Fog":
        return carla.WeatherParameters(
            cloudiness=60.0, fog_density=65.0, fog_distance=8.0,
            fog_falloff=0.2, sun_altitude_angle=35.0,
        )
    if name not in presets:
        raise ValueError(f"Unsupported weather setting: {name}.")
    return presets[name]


def blueprint(library, identifier, prefix):
    if not identifier.startswith(prefix):
        raise ValueError(f"Blueprint '{identifier}' must be a {prefix} blueprint.")
    matches = [item for item in library.filter(identifier) if item.id == identifier]
    if not matches:
        raise ValueError(f"Blueprint '{identifier}' is unavailable in the running CARLA server.")
    return matches[0]


def location_json(location):
    return {"x": round(location.x, 3), "y": round(location.y, 3), "z": round(location.z, 3)}


def follow_camera(spectator, ego):
    transform = ego.get_transform()
    forward = transform.get_forward_vector()
    location = carla.Location(
        x=transform.location.x - forward.x * 9.0,
        y=transform.location.y - forward.y * 9.0,
        z=transform.location.z + 5.0,
    )
    spectator.set_transform(carla.Transform(
        location, carla.Rotation(pitch=-20.0, yaw=transform.rotation.yaw),
    ))


def road_ahead(start_waypoint, number, rng, check_stop=lambda: None):
    """Build one drivable route shared by the ego and the obstacle placement."""
    route = [start_waypoint]
    waypoint = start_waypoint
    for _ in range(max(100, number * 4 + 12)):
        check_stop()
        candidates = waypoint.next(2.0)
        if not candidates:
            break
        candidates = sorted(candidates, key=lambda item: (item.road_id, item.lane_id, item.s))
        waypoint = candidates[rng.randrange(len(candidates))]
        route.append(waypoint)
    indices = [11 + index * 4 for index in range(number)]
    if len(route) < 3 or (indices and indices[-1] >= len(route)):
        raise RuntimeError("The road ends before all obstacles can be placed.")
    positions = [route[index].transform for index in indices]
    # Prevent an extremely long looping road from placing props on one another.
    for index, position in enumerate(positions):
        if any(position.location.distance(existing.location) < 5.0 for existing in positions[:index]):
            raise RuntimeError("The road loops before all obstacles can be placed without overlap.")
    return route, positions


def configure_behavior_agent(ego, route):
    try:
        from agents.navigation.behavior_agent import BehaviorAgent
        from agents.navigation.local_planner import RoadOption
    except ImportError as exception:
        raise RuntimeError(
            "Behavior Agent was selected, but CARLA PythonAPI/carla/agents or its "
            "dependencies could not be imported. Check PYTHONPATH and the worker Python environment."
        ) from exception
    agent = BehaviorAgent(ego, behavior="normal")
    # These are actual sampled drivable waypoints. LANEFOLLOW lets the local
    # controller follow the explicit path through junctions without a second
    # shortest-path search that might skip the configured obstacle placements.
    agent.set_global_plan([(waypoint, RoadOption.LANEFOLLOW) for waypoint in route])
    return agent


def run(config_path, host, port, traffic_manager_port, stop_file=None):
    client = None
    world = None
    traffic_manager = None
    previous_settings = None
    previous_weather = None
    previous_camera = None
    actors = []
    controllers = []
    started_controllers = []
    sensors = []
    collisions = []
    collision_lock = threading.Lock()
    spawned = {"ego": 0, "traffic": 0, "pedestrians": 0, "obstacles": 0}
    actor_manifest = []
    evidence = []
    oracle_results = []
    failure = None
    elapsed = 0.0
    distance_travelled = 0.0
    max_speed = 0.0
    max_speed_excess = 0.0
    max_sustained_speed_excess = 0.0
    max_stationary_seconds = 0.0
    stationary_seconds = 0.0
    seed = None
    agent_name = None
    route_summary = None
    cleanup_errors = []
    duration = None
    rules = []
    requested = {}
    telemetry = {}
    manifest = None
    manifest_path = Path(config_path).with_name("cleanup-manifest.json")
    stop_path = Path(stop_file) if stop_file else Path(config_path).with_name("stop.requested")
    ownership = uuid.uuid4().hex

    def check_stop():
        if stop_path.exists():
            raise KeyboardInterrupt("Simulation cancelled by the host stop request.")

    def phase(name, **details):
        print("CARLA_SETUP " + json.dumps({"phase": name, **details}), flush=True)
        check_stop()

    def owned(actor, kind):
        # Write each server-assigned ID immediately so a recovery process can
        # remove only resources created by this run after an abrupt host kill.
        manifest["actors"].append({
            "id": actor.id, "blueprint": actor.type_id,
            "role": actor.attributes.get("role_name"),
            "parentId": actor.parent.id if actor.parent else None,
            "kind": kind, "released": False,
        })
        save_manifest(manifest_path, manifest)

    def role_for(actor_blueprint, kind):
        if actor_blueprint.has_attribute("role_name"):
            actor_blueprint.set_attribute("role_name", f"autoscenariox_{ownership}_{kind}")

    try:
        check_stop()
        with open(config_path, "r", encoding="utf-8-sig") as source:
            payload = json.load(source)
        config = value(payload, "scenario", payload)
        seed = integer(payload, "randomSeed", 42, -2147483648, 2147483647)
        carla_seed = seed % (2 ** 32)
        rng = random.Random(seed)
        random.seed(seed)  # The official agents also use Python's global generator.
        agent_name = value(payload, "agent", "Autopilot")
        if agent_name not in {"Autopilot", "Behavior Agent"}:
            raise ValueError(f"Unsupported driving agent: {agent_name}.")
        raw_rules = value(payload, "oracleRules", None)
        rules = ([rule for rule in raw_rules if value(rule, "enabled", True)]
                 if raw_rules is not None else [{"id": "collision", "threshold": 0}])
        if not rules:
            raise ValueError("Enable at least one supported native oracle: collision, speed, speeding or stagnation.")
        unsupported = sorted({value(rule, "id", "unknown") for rule in rules} - SUPPORTED_RULES)
        if unsupported:
            raise ValueError(
                "Native actor execution cannot evaluate these enabled oracles: "
                + ", ".join(unsupported)
                + ". Disable them for this native run or use an imported executable OpenSCENARIO file."
            )
        for rule in rules:
            threshold = float(value(rule, "threshold", 0))
            if not math.isfinite(threshold) or threshold < 0:
                raise ValueError("Oracle thresholds must be finite, non-negative numbers.")

        duration = integer(config, "executionDurationSeconds", 30, 5, 600)
        requested = {
            "ego": 1,
            "traffic": integer(config, "trafficVehicleCount", 0, 0, 50),
            "pedestrians": integer(config, "pedestrianCount", 0, 0, 100),
            "obstacles": integer(config, "obstacleCount", 0, 0, 100),
        }
        speed_offset = integer(config, "speedLimitOffsetPercent", 0, -30, 70)
        selected_weather = weather_for(value(config, "weather", "Clear noon"))
        phase("connecting", host=host, port=port)
        client = carla.Client(host, port)
        client.set_timeout(30.0)
        world = client.get_world()
        requested_map = value(config, "map", "Town01")
        if requested_map.rsplit("/", 1)[-1] != world.get_map().name.rsplit("/", 1)[-1]:
            phase("loading_map", map=requested_map)
            world = client.load_world(requested_map)
        phase("map_ready", map=world.get_map().name, worldId=world.id)
        previous_settings = world.get_settings()
        previous_weather = world.get_weather()
        spectator = world.get_spectator()
        previous_camera = spectator.get_transform()
        manifest = {
            "formatVersion": 1, "ownership": ownership, "ownerPid": os.getpid(),
            "host": host, "port": port, "trafficManagerPort": traffic_manager_port,
            "worldId": world.id, "map": world.get_map().name,
            "originalSettings": critical_settings(previous_settings),
            "actors": [], "complete": False, "settingsRestored": False,
        }
        save_manifest(manifest_path, manifest)
        settings = world.get_settings()
        settings.synchronous_mode = True
        settings.fixed_delta_seconds = FIXED_DELTA_SECONDS
        settings.no_rendering_mode = False
        settings.substepping = True
        settings.max_substep_delta_time = 0.01
        settings.max_substeps = 10
        world.apply_settings(settings)
        phase("world_settings_applied")
        world.set_weather(selected_weather)
        world.set_pedestrians_seed(carla_seed)
        world.set_pedestrians_cross_factor(1.0)
        traffic_manager = client.get_trafficmanager(traffic_manager_port)
        traffic_manager.set_synchronous_mode(True)
        traffic_manager.set_random_device_seed(carla_seed)
        phase("traffic_manager_ready", trafficManagerPort=traffic_manager_port)
        library = world.get_blueprint_library()
        ego_blueprint = blueprint(library, value(config, "egoVehicle", "vehicle.tesla.model3"), "vehicle.")
        role_for(ego_blueprint, "ego")
        carla_map = world.get_map()
        spawn_points = sorted(carla_map.get_spawn_points(), key=lambda point: (
            point.location.x, point.location.y, point.location.z,
        ))
        if not spawn_points:
            raise RuntimeError("The selected CARLA map has no vehicle spawn points.")
        rng.shuffle(spawn_points)
        phase("spawning_ego", spawnPoints=len(spawn_points))

        def remember(actor, category, location):
            actors.append(actor)
            owned(actor, category)
            actor_manifest.append({
                "actorId": actor.id, "blueprint": actor.type_id, "category": category,
                "spawn": location_json(location),
            })
            spawned[category] += 1

        ego = None
        planned_route = []
        obstacle_positions = []
        for transform in spawn_points:
            check_stop()
            waypoint = carla_map.get_waypoint(transform.location, project_to_road=True, lane_type=carla.LaneType.Driving)
            if waypoint is None:
                continue
            try:
                candidate_route, candidate_obstacles = road_ahead(waypoint, requested["obstacles"], rng, check_stop)
            except RuntimeError:
                continue
            ego = world.try_spawn_actor(ego_blueprint, transform)
            if ego is not None:
                planned_route = candidate_route
                obstacle_positions = candidate_obstacles
                break
        if ego is None:
            raise RuntimeError("CARLA could not spawn the ego vehicle on a road with enough space for the configured obstacles.")
        remember(ego, "ego", transform.location)
        ego.apply_control(carla.VehicleControl(brake=1.0))
        origin = transform.location
        collision_blueprint = blueprint(library, "sensor.other.collision", "sensor.")
        role_for(collision_blueprint, "collision")
        collision_sensor = world.spawn_actor(collision_blueprint, carla.Transform(), attach_to=ego)
        sensors.append(collision_sensor)
        owned(collision_sensor, "sensor")

        def record_collision(event):
            impulse = event.normal_impulse
            with collision_lock:
                collisions.append({
                    "otherActor": event.other_actor.type_id, "otherActorId": event.other_actor.id,
                    "frame": event.frame, "simulationTimestamp": round(event.timestamp, 3),
                    "normalImpulse": round(math.sqrt(impulse.x ** 2 + impulse.y ** 2 + impulse.z ** 2), 3),
                })

        collision_sensor.listen(record_collision)
        phase("ego_and_sensor_ready", ego=ego.id, sensor=collision_sensor.id)
        follow_camera(spectator, ego)
        traffic_actors = []
        if requested["traffic"]:
            traffic_blueprint = blueprint(library, value(config, "trafficVehicle", "vehicle.lincoln.mkz_2020"), "vehicle.")
            role_for(traffic_blueprint, "traffic")
            for traffic_transform in sorted(spawn_points, key=lambda point: origin.distance(point.location)):
                check_stop()
                if spawned["traffic"] >= requested["traffic"]:
                    break
                if origin.distance(traffic_transform.location) < 12.0:
                    continue
                vehicle = world.try_spawn_actor(traffic_blueprint, traffic_transform)
                if vehicle is not None:
                    remember(vehicle, "traffic", traffic_transform.location)
                    vehicle.apply_control(carla.VehicleControl(brake=1.0))
                    traffic_actors.append(vehicle)
            if spawned["traffic"] != requested["traffic"]:
                raise RuntimeError(f"CARLA could not spawn all traffic vehicles ({spawned['traffic']}/{requested['traffic']}).")
        phase("traffic_spawned", count=spawned["traffic"])

        if requested["obstacles"]:
            obstacle_blueprint = blueprint(library, value(config, "obstacleBlueprint", "static.prop.streetbarrier"), "static.prop.")
            role_for(obstacle_blueprint, "obstacle")
            for obstacle_transform in obstacle_positions:
                check_stop()
                position = carla.Location(x=obstacle_transform.location.x, y=obstacle_transform.location.y, z=obstacle_transform.location.z + 0.2)
                obstacle = world.try_spawn_actor(obstacle_blueprint, carla.Transform(position, obstacle_transform.rotation))
                if obstacle is None:
                    raise RuntimeError(f"CARLA could not place all obstacles ({spawned['obstacles']}/{requested['obstacles']}). Try fewer obstacles or another map.")
                remember(obstacle, "obstacles", position)
                obstacle.set_simulate_physics(False)
        phase("obstacles_spawned", count=spawned["obstacles"])

        walkers_to_start = []
        if requested["pedestrians"]:
            walker_blueprint = blueprint(library, value(config, "pedestrianBlueprint", "walker.pedestrian.0001"), "walker.pedestrian.")
            role_for(walker_blueprint, "pedestrian")
            if walker_blueprint.has_attribute("is_invincible"):
                walker_blueprint.set_attribute("is_invincible", "false")
            controller_blueprint = blueprint(library, "controller.ai.walker", "controller.")
            role_for(controller_blueprint, "walker_controller")
            locations = []
            phase("sampling_pedestrian_navigation", requested=requested["pedestrians"])
            # Pool map-wide navigation samples, then choose the closest ones.
            for _ in range(max(300, requested["pedestrians"] * 25)):
                check_stop()
                point = world.get_random_location_from_navigation()
                if point is not None and origin.distance(point) <= 150.0:
                    if all(point.distance(existing) >= 1.0 for existing in locations):
                        locations.append(point)
            locations.sort(key=origin.distance)
            phase("pedestrian_navigation_ready", candidates=len(locations))
            for location in locations:
                check_stop()
                if spawned["pedestrians"] >= requested["pedestrians"]:
                    break
                walker = world.try_spawn_actor(walker_blueprint, carla.Transform(location))
                if walker is None:
                    continue
                actors.append(walker)
                owned(walker, "pedestrian")
                controller = world.try_spawn_actor(controller_blueprint, carla.Transform(), attach_to=walker)
                if controller is None:
                    walker.destroy()
                    actors.remove(walker)
                    continue
                actors.append(controller)
                controllers.append(controller)
                owned(controller, "controller")
                actor_manifest.append({
                    "actorId": walker.id, "blueprint": walker.type_id, "category": "pedestrians",
                    "spawn": location_json(location),
                })
                spawned["pedestrians"] += 1
                walkers_to_start.append((walker, controller))
                phase("pedestrian_spawned", walker=walker.id, controller=controller.id)
            if spawned["pedestrians"] != requested["pedestrians"]:
                raise RuntimeError(
                    f"CARLA could not place all pedestrians near the scenario ({spawned['pedestrians']}/{requested['pedestrians']}). "
                    "Try fewer pedestrians or a map with nearby navigation mesh."
                )
            # Official generate_traffic.py requires a tick before controller.start().
            world.tick(10.0)
            phase("starting_pedestrian_controllers")
            for walker, controller in walkers_to_start:
                check_stop()
                destinations = [point for point in locations if walker.get_location().distance(point) > 8.0]
                if not destinations:
                    raise RuntimeError("CARLA has no nearby pedestrian navigation destination.")
                controller.start()
                started_controllers.append(controller)
                controller.go_to_location(rng.choice(destinations))
                controller.set_max_speed(rng.uniform(0.8, 1.8))
        phase("pedestrians_ready", count=spawned["pedestrians"])

        gap = {"Low": 9.0, "Medium": 5.0, "High": 2.5}.get(value(config, "trafficDensity", "Medium"), 5.0)
        for vehicle in traffic_actors:
            check_stop()
            vehicle.set_autopilot(True, traffic_manager_port)
            traffic_manager.vehicle_percentage_speed_difference(vehicle, speed_offset)
            traffic_manager.distance_to_leading_vehicle(vehicle, gap)
        behavior_agent = None
        # Ensure the actor snapshot is current before planning a route.
        world.tick(10.0)
        phase("configuring_ego_agent", agent=agent_name)
        route_summary = {
            "kind": "Seeded CARLA drivable road path shared with obstacle placement",
            "destination": location_json(planned_route[-1].transform.location),
            "waypoints": len(planned_route),
            "path": [location_json(waypoint.transform.location) for waypoint in planned_route],
        }
        if agent_name == "Behavior Agent":
            check_stop()
            behavior_agent = configure_behavior_agent(ego, planned_route)
        else:
            ego.set_autopilot(True, traffic_manager_port)
            traffic_manager.vehicle_percentage_speed_difference(ego, speed_offset)
            traffic_manager.distance_to_leading_vehicle(ego, gap)
            traffic_manager.set_path(ego, [waypoint.transform.location for waypoint in planned_route], True)
        phase("ego_agent_ready")
        initial_snapshot = world.get_snapshot()
        initial_simulation_time = initial_snapshot.timestamp.elapsed_seconds
        previous_simulation_time = initial_simulation_time
        previous_location = ego.get_location()
        previous_frame = initial_snapshot.frame
        wall_started = time.monotonic()
        next_progress_at = 0.0
        route_completed = False
        trace = []
        speed_window = deque()
        print("CARLA_ACTORS " + json.dumps({"seed": seed, "agent": agent_name, "spawned": spawned, "actors": actor_manifest, "route": route_summary}), flush=True)

        while elapsed + 0.0001 < duration:
            check_stop()
            if not ego.is_alive:
                raise RuntimeError("The ego vehicle was destroyed during the simulation.")
            if behavior_agent is not None:
                route_completed = behavior_agent.done()
                ego.apply_control(carla.VehicleControl(brake=1.0) if route_completed else behavior_agent.run_step())
            world.tick(10.0)
            snapshot = world.get_snapshot()
            if snapshot.frame <= previous_frame:
                raise RuntimeError("CARLA did not advance a simulation frame.")
            now = snapshot.timestamp.elapsed_seconds
            delta = now - previous_simulation_time
            if delta <= 0:
                raise RuntimeError("CARLA did not advance simulation time.")
            if snapshot.frame != previous_frame + 1:
                raise RuntimeError("Another CARLA client advanced the shared simulation. Stop other ticking clients and retry.")
            elapsed = now - initial_simulation_time
            previous_simulation_time = now
            previous_frame = snapshot.frame
            location = ego.get_location()
            distance_travelled += previous_location.distance(location)
            previous_location = location
            velocity = ego.get_velocity()
            speed = 3.6 * math.sqrt(velocity.x ** 2 + velocity.y ** 2 + velocity.z ** 2)
            limit = ego.get_speed_limit()
            excess = max(0.0, speed - limit) if limit > 0 else 0.0
            max_speed = max(max_speed, speed)
            max_speed_excess = max(max_speed_excess, excess)
            speed_window.append((elapsed, excess))
            while len(speed_window) > 1 and speed_window[1][0] <= elapsed - 1.0:
                speed_window.popleft()
            if speed_window and elapsed - speed_window[0][0] >= 1.0 - 0.0001:
                max_sustained_speed_excess = max(max_sustained_speed_excess, min(item[1] for item in speed_window))
            waiting_at_red = ego.is_at_traffic_light() and ego.get_traffic_light_state() == carla.TrafficLightState.Red
            stationary_seconds = (stationary_seconds + delta
                                  if elapsed > 2.0 and speed < 0.5 and not waiting_at_red and not route_completed
                                  else 0.0)
            max_stationary_seconds = max(max_stationary_seconds, stationary_seconds)
            follow_camera(spectator, ego)
            if elapsed >= next_progress_at:
                with collision_lock:
                    collision_count = len(collisions)
                sample = {"simulationSeconds": round(elapsed, 3), "speedKmh": round(speed, 3),
                          "speedLimitKmh": round(limit, 3), "position": location_json(location)}
                trace.append(sample)
                print("CARLA_PROGRESS " + json.dumps({**sample, "durationSeconds": duration,
                      "spawned": spawned, "collisions": collision_count}), flush=True)
                next_progress_at += 1.0
            # Fast machines still show a visible run near real time; duration is
            # measured from CARLA snapshots rather than the wall clock.
            pause = elapsed - (time.monotonic() - wall_started)
            if pause > 0:
                time.sleep(min(pause, FIXED_DELTA_SECONDS))

        if collision_sensor.is_listening:
            collision_sensor.stop()
        with collision_lock:
            collision_events = list(collisions)
        timestamp = datetime.now(timezone.utc).isoformat()
        for rule in rules:
            rule_id = value(rule, "id")
            threshold = float(value(rule, "threshold", 0))
            if rule_id == "collision":
                measured = len(collision_events)
                note = "Ego collision sensor contact events. Consecutive contact frames count as CARLA reports them."
            elif rule_id == "speeding":
                measured = round(max_speed, 3)
                note = "Maximum measured ego speed in km/h; threshold is an absolute maximum."
            elif rule_id == "speed":
                measured = round(max_sustained_speed_excess, 3)
                note = "Maximum road-limit excess in km/h maintained throughout a continuous one-second simulation window."
            else:
                measured = round(max_stationary_seconds, 3)
                note = "Longest stationary period below 0.5 km/h, excluding the first 2s, known red-light waits and completed Behavior Agent routes."
            passed = measured <= threshold
            oracle_results.append({"rule": rule_id, "measured": measured, "threshold": threshold, "passed": passed, "note": note})
            evidence.append({"timestamp": timestamp, "rule": rule_id, "measured": str(measured), "threshold": str(threshold), "note": note})
        failed = [item["rule"] for item in oracle_results if not item["passed"]]
        if failed:
            failure = "Measured oracle violation: " + ", ".join(failed) + "."
        if elapsed < duration - FIXED_DELTA_SECONDS:
            failure = "The requested simulation duration was not completed."
        telemetry = {
            "simulationSeconds": round(elapsed, 3), "distanceTravelledMeters": round(distance_travelled, 3),
            "maximumSpeedKmh": round(max_speed, 3), "maximumSpeedExcessKmh": round(max_speed_excess, 3),
            "maximumSustainedSpeedExcessKmh": round(max_sustained_speed_excess, 3),
            "maximumStationarySeconds": round(max_stationary_seconds, 3), "samples": trace,
        }
    except (Exception, KeyboardInterrupt) as exception:
        failure = str(exception) or "Simulation cancelled."
        telemetry = {"simulationSeconds": round(elapsed, 3), "distanceTravelledMeters": round(distance_travelled, 3)}
    finally:
        print("CARLA_CLEANUP_PHASE stopping_controllers", flush=True)
        for controller in started_controllers:
            try:
                controller.stop()
            except RuntimeError:
                pass
        print("CARLA_CLEANUP_PHASE stopping_sensors", flush=True)
        for sensor in sensors:
            try:
                if sensor.is_listening:
                    sensor.stop()
            except RuntimeError:
                pass
        print("CARLA_CLEANUP_PHASE destroying_owned_actors", flush=True)
        destroy_order = list(reversed(sensors)) + list(reversed(actors))
        # Destroy attached actors before parents using the same server-side batch
        # command as CARLA's official traffic example. Avoid repeated Sensor.stop.
        for actor in destroy_order:
            try:
                if actor.is_alive:
                    response = client.apply_batch_sync([carla.command.DestroyActor(actor.id)], False)[0]
                    if response.has_error():
                        raise RuntimeError(f"CARLA did not destroy owned actor {actor.id}: {response.error}")
                if manifest is not None:
                    for entry in manifest["actors"]:
                        if entry["id"] == actor.id:
                            entry["released"] = True
                    save_manifest(manifest_path, manifest)
            except (RuntimeError, OSError) as exception:
                cleanup_errors.append(str(exception))
        if traffic_manager is not None:
            print("CARLA_CLEANUP_PHASE restoring_traffic_manager", flush=True)
            try:
                # CARLA 0.9.16 has no get_synchronous_mode(). Restore this worker's
                # dedicated TM consistently with the saved world settings.
                traffic_manager.set_synchronous_mode(bool(previous_settings and previous_settings.synchronous_mode))
            except RuntimeError as exception:
                cleanup_errors.append(str(exception))
        if world is not None and previous_settings is not None:
            print("CARLA_CLEANUP_PHASE restoring_world", flush=True)
            try:
                if previous_weather is not None:
                    world.set_weather(previous_weather)
                if previous_camera is not None:
                    world.get_spectator().set_transform(previous_camera)
                world.apply_settings(previous_settings)
                if manifest is not None:
                    manifest["settingsRestored"] = True
            except RuntimeError as exception:
                cleanup_errors.append(str(exception))
        if manifest is not None:
            print("CARLA_CLEANUP_PHASE completing_manifest", flush=True)
            try:
                # A walker whose controller failed may already have been removed.
                for entry in manifest["actors"]:
                    if not entry["released"]:
                        actor = world.get_actor(entry["id"])
                        entry["released"] = actor is None or not actor.is_alive
                manifest["complete"] = (not cleanup_errors and manifest["settingsRestored"]
                                        and all(entry["released"] for entry in manifest["actors"]))
                manifest["cleanupErrors"] = cleanup_errors
                save_manifest(manifest_path, manifest)
            except (RuntimeError, OSError) as exception:
                cleanup_errors.append(str(exception))

    if cleanup_errors and failure is None:
        failure = "Simulation finished, but CARLA resources could not be fully cleaned up."
    result = {
        "outcome": "PASS" if failure is None else "FAIL",
        "reason": failure or "The actors ran for the requested simulation duration and every enabled native oracle passed.",
        "seed": seed, "agent": agent_name, "requested": requested, "spawned": spawned,
        "durationSeconds": duration, "actors": actor_manifest, "collisions": list(collisions),
        "oracleResults": oracle_results, "evidence": evidence, "telemetry": telemetry,
        "route": route_summary, "cleanupErrors": cleanup_errors,
        "limitations": [
            "Route, trigger and action descriptions are documentation; arbitrary prose is not executed.",
            "Native oracles support collision, absolute speeding, sustained excess speed and stagnation only.",
            "Both agents follow the recorded seeded road path; Traffic Manager can resume roaming after exhausting it.",
            "Speed offset applies to Traffic Manager vehicles; the official Behavior Agent keeps its own speed policy.",
            "Pedestrians navigate nearby sidewalks with crossing enabled; prose does not define a custom trajectory.",
        ],
    }
    try:
        Path(config_path).with_name("carla-result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    except OSError as exception:
        result["resultFileError"] = str(exception)
    print("AUTOSCENARIOX_RESULT:" + json.dumps(result), flush=True)
    return 0 if failure is None else 2


def stop_requested(*_):
    raise KeyboardInterrupt("Simulation cancelled by the host.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=2000)
    parser.add_argument("--traffic-manager-port", type=int, default=8000)
    parser.add_argument("--stop-file")
    parser.add_argument("--cleanup", action="store_true")
    arguments = parser.parse_args()
    signal.signal(signal.SIGINT, stop_requested)
    if hasattr(signal, "SIGTERM"):
        signal.signal(signal.SIGTERM, stop_requested)
    sys.exit(cleanup(arguments.config, arguments.host, arguments.port) if arguments.cleanup else
             run(arguments.config, arguments.host, arguments.port, arguments.traffic_manager_port, arguments.stop_file))
