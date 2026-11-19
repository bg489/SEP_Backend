#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Ghi Telemetry thật (asx.telemetry/1) từ CARLA để thử Oracle. Không phải runner của Flow 2.

Đọc một gói chạy đã lưu (GET /api/v1/scenario-versions/{id}/execution-package), dựng scenario trong
CARLA 0.9.16 ở synchronous mode, cho BehaviorAgent lái ego, ghi mỗi tick ra recordings/*.jsonl.
Hợp đồng: docs/telemetry-format.md (mục 4: cách dùng). Dừng sớm khi Collision hoặc Red-light: docs/adr/0001-offline-versioned-oracle.md.

Cài đặt (Windows cmd, Python 3.12, bản 0.9.16 chưa có wheel cho Python mới hơn):
    py -3.12 -m venv .venv && .venv\\Scripts\\activate
    set CARLA_ROOT=<CARLA installation directory>
    pip install carla==0.9.16
    pip install -r %CARLA_ROOT%\\PythonAPI\\carla\\requirements.txt
    %CARLA_ROOT%\\CarlaUE4.exe -quality-level=Low -windowed -ResX=1280 -ResY=720
    (camera của cửa sổ CARLA tự bám sau ego; thêm -RenderOffScreen nếu không cần xem, nhẹ GPU hơn)

Chạy:
    python tools/carla_recorder.py package.json --profile normal

Đã hiện thực: route (start, waypoints, goal), weather, ego, traffic nền (TM có seed), actors,
va chạm, vượt đèn đỏ (đầu xe cắt vạch dừng).
Supports all five schema event types, all five triggers, and campaign-selected BehaviorAgent/Autopilot.
Unsupported or untriggered scenario actions fail explicitly instead of being silently skipped.
Runner của Flow 2 có thể gọi record(pkg, profile, ..., run_id=<simulation run id>, seed=...) rồi nộp file qua
PUT /api/v1/runs/{id}/telemetry (docs/oracle-api.md).
"""
import argparse
import json
import math
import os
import random
import sys
import uuid
from carla_maps import normalize_map_name

FORMAT = "asx.telemetry/1"
ACTOR_RADIUS_M = 50.0
# Đuôi sau đèn đỏ / va chạm. Oracle bỏ qua tick post_stop nên điểm không đổi;
# video vẫn thấy xe đi qua tình huống.
POST_STOP_S = 10.0
LIGHT_STATES = {"red", "yellow", "green"}
SUPPORTED_EVENTS = {"pedestrian_crossing", "lead_vehicle_brake", "vehicle_cut_in", "traffic_light_override", "vehicle_crossing"}
SUPPORTED_TRIGGERS = {"simulation_time", "ego_distance_to_actor", "ego_distance_to_location", "after_event", "time_to_collision"}


# ---------- phần thuần (kiểm ở tools/test_carla_recorder.py) ----------

def resolve_location(loc, path, locations):
    """Vị trí dạng spawn index lấy toạ độ đã giải trong gói (đúng thứ tự spawn point của map); dạng toạ độ dùng thẳng."""
    if "spawn_point_index" in loc:
        if path not in locations:
            raise ValueError(f"Execution package has no location for {path}.")
        return locations[path]
    return {"x": loc["x"], "y": loc["y"], "z": loc.get("z", 0.5), "yaw": loc.get("yaw", 0.0)}


def trigger_fired(trigger, t, ego, actors, where, event_times=None, ego_velocity=None, velocities=None):
    """ego, actors[ref]: (x, y, z); where: vị trí đã giải của ego_distance_to_location."""
    kind = trigger["type"]
    if kind == "simulation_time":
        return t >= trigger["time_s"]
    if kind == "after_event":
        previous = (event_times or {}).get(trigger["event_ref"])
        return previous is not None and t >= previous + trigger.get("delay_s", 0)
    if kind == "time_to_collision":
        ref = trigger["actor_ref"]
        target = actors[ref]
        if ego_velocity is None or ref not in (velocities or {}):
            raise ValueError("TTC trigger requires measured ego and actor velocities")
        if not all(math.isfinite(value) for vector in (ego, target, ego_velocity, velocities[ref]) for value in vector):
            raise ValueError("TTC trigger requires finite measured positions and velocities")
        delta = tuple(target[i] - ego[i] for i in range(3))
        distance = math.sqrt(sum(value * value for value in delta))
        if distance <= 1e-6:
            return True
        relative = tuple(ego_velocity[i] - velocities[ref][i] for i in range(3))
        closing = sum(relative[i] * delta[i] for i in range(3)) / distance
        return closing > 0 and distance / closing <= trigger["ttc_s"]
    if kind not in ("ego_distance_to_actor", "ego_distance_to_location"):
        raise ValueError(f"Unsupported trigger: {kind}")
    target = actors[trigger["actor_ref"]] if kind == "ego_distance_to_actor" else (where["x"], where["y"], where["z"])
    return math.dist(ego, target) <= trigger["distance_m"]


def impact_side(fwd, right, impulse):
    """Impulse đẩy ego ra xa chỗ va chạm, nên chỗ va chạm nằm theo hướng -impulse."""
    f = -(impulse[0] * fwd[0] + impulse[1] * fwd[1])
    s = -(impulse[0] * right[0] + impulse[1] * right[1])
    if abs(s) > abs(f):
        return "right" if s > 0 else "left"
    return "rear" if f < 0 else "front"


def chase_camera(x, y, z, yaw, back=8.0, up=3.5, pitch=-15.0):
    """Vị trí camera bám sau ego (x, y, z, pitch, yaw) theo hướng yaw (độ) của ego."""
    rad = math.radians(yaw)
    return x - back * math.cos(rad), y - back * math.sin(rad), z + up, pitch, yaw


def front_bumper(x, y, yaw_deg, extent_x):
    """Điểm đầu xe. extent_x là nửa chiều dài bounding box (m)."""
    rad = math.radians(yaw_deg)
    return (x + math.cos(rad) * extent_x, y + math.sin(rad) * extent_x)


def stop_line_segment(x, y, yaw_deg, half_width):
    """Vạch dừng ngang làn tại waypoint dừng. yaw là hướng xe phải chạy."""
    rad = math.radians(yaw_deg)
    rx, ry = -math.sin(rad), math.cos(rad)
    return (
        (x - rx * half_width, y - ry * half_width),
        (x + rx * half_width, y + ry * half_width),
    )


def _segments_cross(a, b, c, d):
    """Hai đoạn thẳng cắt nhau tại một điểm nằm trong cả hai đoạn, không chỉ chạm đầu mút."""
    def cross(p, q, r):
        return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])

    return cross(a, b, c) * cross(a, b, d) < 0 and cross(c, d, a) * cross(c, d, b) < 0


def crossed_stop_line(prev_front, front, stop_lines):
    """True khi đầu xe đi xuyên một vạch dừng. Dừng trước vạch thì False."""
    if prev_front is None:
        return False
    return any(_segments_cross(prev_front, front, a, b) for a, b in stop_lines)


# Dừng sát vạch, kể cả trôi thêm một đoạn khi phanh, không phải vượt đèn.
# Chỉ tính vượt đèn khi vẫn chạy thêm quãng này trong lúc đèn đỏ.
RED_STOP_SPEED_MS = 0.5
RED_RUN_THROUGH_M = 8.0


def judge_red_light(pending, light_is_red, speed_ms, x, y):
    """pending là None hoặc (x0, y0, stopped) tại lúc cắt vạch.

    Trả (pending mới, ran_through). Cắt vạch rồi dừng thì ran_through là False.
    Chạy tiếp hơn RED_RUN_THROUGH_M mét khi đèn vẫn đỏ thì True.
    """
    if pending is None:
        return None, False
    x0, y0, stopped = pending
    if not light_is_red:
        return None, False
    dist = math.dist((x0, y0), (x, y))
    if speed_ms < RED_STOP_SPEED_MS and dist < RED_RUN_THROUGH_M:
        return (x0, y0, True), False
    if dist >= RED_RUN_THROUGH_M and speed_ms >= RED_STOP_SPEED_MS:
        return None, True
    return (x0, y0, stopped), False


def _yaw_delta(a, b):
    return abs((a - b + 180.0) % 360.0 - 180.0)


def _stop_lines(tl, ego_location, ego_yaw):
    """Vạch dừng của đèn đang ảnh hưởng ego, cùng hướng và cách xe không quá 20 m."""
    lines = []
    try:
        waypoints = tl.get_stop_waypoints()
    except Exception:
        return lines
    for wp in waypoints:
        loc = wp.transform.location
        if ego_location.distance(loc) > 20.0:
            continue
        if _yaw_delta(wp.transform.rotation.yaw, ego_yaw) > 60.0:
            continue
        half = 2.0
        width = getattr(wp, "lane_width", 0) or 0
        if width > 0:
            half = max(1.6, float(width) / 2.0)
        lines.append(stop_line_segment(loc.x, loc.y, wp.transform.rotation.yaw, half))
    return lines


def evidence_camera(x, y, z, yaw, light_xyz=None):
    """Camera chéo sau xe, nhìn về phía trước để xe và đèn cùng nằm trong khung."""
    rad = math.radians(yaw)
    fx, fy = math.cos(rad), math.sin(rad)
    rx, ry = -math.sin(rad), math.cos(rad)
    cx = x - 5.0 * fx + 7.0 * rx
    cy = y - 5.0 * fy + 7.0 * ry
    cz = z + 4.0
    ax = x + 16.0 * fx
    ay = y + 16.0 * fy
    az = z + 2.2
    if light_xyz is not None:
        lx, ly, lz = light_xyz
        ax = 0.7 * ax + 0.3 * lx
        ay = 0.7 * ay + 0.3 * ly
        az = 0.7 * az + 0.3 * lz
    dist = math.hypot(ax - cx, ay - cy) or 1.0
    look = math.degrees(math.atan2(ay - cy, ax - cx))
    pitch = math.degrees(math.atan2(az - cz, dist))
    return cx, cy, cz, pitch, look


def write_mp4(path, frames_bgr, fps=12):
    """Ghi mp4 H.264 (trình duyệt phát được). Trả path nếu OK, None nếu lỗi."""
    if not frames_bgr:
        return None
    try:
        import cv2
    except ImportError:
        print("warning: opencv-python not installed — skip video", file=sys.stderr)
        return None
    h, w = frames_bgr[0].shape[:2]
    # yuv420p cần kích thước chẵn.
    w -= w % 2
    h -= h % 2
    if w < 2 or h < 2:
        return None
    raw = path + ".raw.mp4"
    fourcc = cv2.VideoWriter_fourcc(*"mp4v")
    writer = cv2.VideoWriter(raw, fourcc, float(fps), (w, h))
    if not writer.isOpened():
        print(f"warning: could not open VideoWriter for {raw}", file=sys.stderr)
        return None
    for fr in frames_bgr:
        if fr.shape[0] != h or fr.shape[1] != w:
            fr = cv2.resize(fr, (w, h))
        writer.write(fr)
    writer.release()
    try:
        import imageio_ffmpeg
        import subprocess
        ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
        proc = subprocess.run(
            [ffmpeg, "-y", "-i", raw, "-c:v", "libx264", "-pix_fmt", "yuv420p",
             "-movflags", "+faststart", "-an", path],
            capture_output=True, text=True,
        )
        if proc.returncode != 0 or not os.path.isfile(path) or os.path.getsize(path) < 1000:
            print(f"warning: h264 encode failed: {proc.stderr[-400:]}", file=sys.stderr)
            return None
        return path
    except Exception as ex:  # noqa: BLE001
        print(f"warning: h264 encode failed: {ex}", file=sys.stderr)
        return None
    finally:
        try:
            os.remove(raw)
        except OSError:
            pass


def _find_carla_hwnd():
    """HWND cửa sổ CarlaUE4. None nếu không thấy."""
    try:
        import ctypes
        from ctypes import wintypes
    except ImportError:
        return None
    user32 = ctypes.windll.user32
    found = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def _enum(hwnd, _lparam):
        length = user32.GetWindowTextLengthW(hwnd)
        buf = ctypes.create_unicode_buffer(max(length, 0) + 1)
        user32.GetWindowTextW(hwnd, buf, max(length, 0) + 1)
        title = (buf.value or "").lower()
        if "carlaue4" in title:
            found.append(hwnd)
        return True

    user32.EnumWindows(_enum, 0)
    return found[0] if found else None


def _focus_carla(hwnd):
    """Đưa cửa sổ CARLA lên trước. Chụp màn hình lấy pixel đang hiện — cửa sổ bị che sẽ ra hình Cursor."""
    if not hwnd:
        return
    import ctypes
    user32 = ctypes.windll.user32
    user32.ShowWindow(hwnd, 9)  # SW_RESTORE
    user32.keybd_event(0x12, 0, 0, 0)  # ALT down — bỏ khóa foreground của Windows
    user32.SetForegroundWindow(hwnd)
    user32.keybd_event(0x12, 0, 2, 0)  # ALT up


def _grab_carla_window():
    """Chụp đúng cửa sổ CarlaUE4 (BGR). Bỏ frame nếu cửa sổ không ở trên cùng."""
    try:
        import ctypes
        from ctypes import wintypes
        import mss
        import numpy as np
    except ImportError:
        return None
    hwnd = _find_carla_hwnd()
    if not hwnd:
        return None
    user32 = ctypes.windll.user32
    if user32.GetForegroundWindow() != hwnd:
        _focus_carla(hwnd)
    rect = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    w, h = rect.right - rect.left, rect.bottom - rect.top
    if w < 80 or h < 80:
        return None
    if user32.GetForegroundWindow() != hwnd:
        return None
    try:
        with mss.mss() as sct:
            shot = sct.grab({"left": int(rect.left), "top": int(rect.top), "width": int(w), "height": int(h)})
            arr = np.asarray(shot)[:, :, :3]
            if arr.shape[1] > 640:
                import cv2
                arr = cv2.resize(arr, (640, max(2, int(arr.shape[0] * 640 / arr.shape[1]))))
            return np.ascontiguousarray(arr)
    except Exception:
        return None


def _stamp_evidence(frame, label):
    """Ghi nhãn lên frame để video tự chỉ ra vi phạm."""
    try:
        import cv2
    except ImportError:
        return frame
    cv2.rectangle(frame, (8, 8), (8 + 16 * max(len(label), 8), 46), (20, 20, 170), -1)
    cv2.putText(frame, label, (16, 36), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2, cv2.LINE_AA)
    return frame


class TelemetryFile:
    """Một file JSON Lines cho một Run. Thoát vì exception mà chưa finish() thì vẫn ghi end INFRA_FAILED."""

    def __init__(self, path, header):
        self._f = open(path, "w", encoding="utf-8", newline="\n")
        self._t = 0.0
        self._ended = False
        self._write({"kind": "header", "format": FORMAT, **header})

    def _write(self, record):
        self._f.write(json.dumps(record, separators=(",", ":"), allow_nan=False) + "\n")
        self._f.flush()

    def tick(self, record):
        self._t = record["t"]
        self._write({"kind": "tick", **record})

    def event(self, record):
        self._write({"kind": "event", **record})

    def finish(self, outcome, reason=None):
        end = {"kind": "end", "t": self._t, "outcome": outcome}
        if reason:
            end["reason"] = reason
        self._write(end)
        self._ended = True

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        if not self._ended:
            self.finish("INFRA_FAILED", str(exc) or (exc_type.__name__ if exc_type else "Run did not finish."))
        self._f.close()
        return False


# ---------- phần CARLA ----------

def _r(v):
    return round(v, 3)


def _type_of(type_id):
    if type_id.startswith("vehicle."):
        return "vehicle"
    if type_id.startswith("walker."):
        return "walker"
    return "static"


def _lane(world_map, loc, carla):
    # Chỉ làn xe chạy ngay dưới actor; người trên vỉa hè không bị gán vào làn gần nhất.
    wp = world_map.get_waypoint(loc, project_to_road=False, lane_type=carla.LaneType.Driving)
    return (wp.road_id, wp.lane_id) if wp else (None, None)


def _transform(carla, p):
    return carla.Transform(carla.Location(x=p["x"], y=p["y"], z=p["z"]), carla.Rotation(yaw=p.get("yaw", 0.0)))


def _clear_world(world):
    """Xóa vehicle/walker/sensor còn sót — tránh spawn đè chỗ cũ."""
    for pattern in ("sensor.*", "vehicle.*", "walker.*"):
        for a in list(world.get_actors().filter(pattern)):
            try:
                a.destroy()
            except Exception:
                pass


def _spawn_vehicle(world, carla, bp, preferred, label="actor"):
    """Scenario positions are exact: a blocked spawn must not silently move the test elsewhere."""
    tf = _transform(carla, preferred) if isinstance(preferred, dict) else preferred
    actor = world.try_spawn_actor(bp, tf)
    if actor is not None:
        world.tick()
        return actor
    raise RuntimeError(f"Spawn failed for {label}: the configured position is blocked")


def _tracked(actor):
    return actor.type_id.startswith(("vehicle.", "walker.pedestrian.", "static.prop."))


def _start_moving(carla, ego, ego_kmh, actor_cfgs, named):
    """Hết warmup: đặt tốc độ ban đầu. Xe scripted giữ vận tốc không đổi đến khi event điều khiển; idle đứng yên."""
    ego.set_target_velocity(ego.get_transform().get_forward_vector() * (ego_kmh / 3.6))
    for a in actor_cfgs:
        kmh = a.get("initial_speed_kmh", 0)
        if not kmh or a["type"] not in ("vehicle", "bicycle"):
            continue
        actor = named[a["id"]]
        if a.get("behavior") == "scripted":
            actor.enable_constant_velocity(carla.Vector3D(kmh / 3.6, 0, 0))   # hệ toạ độ của xe
        elif a.get("behavior") == "autopilot":
            actor.set_target_velocity(actor.get_transform().get_forward_vector() * (kmh / 3.6))


def _fire(carla, event, actor, ego_tf, tm_port, braking, *, tm=None, world=None,
          locations=None, event_index=0, movements=None, light_overrides=None, t=0, ego_speed=0):
    p = event["params"]
    if event["type"] == "pedestrian_crossing":
        # left_to_right: đi theo hướng bên phải của ego lúc trigger
        right = ego_tf.get_right_vector()
        sign = 1 if p.get("direction", "left_to_right") == "left_to_right" else -1
        actor.apply_control(carla.WalkerControl(direction=carla.Vector3D(right.x * sign, right.y * sign, 0), speed=p["speed_ms"]))
    elif event["type"] == "lead_vehicle_brake":
        actor.set_autopilot(False, tm_port)
        actor.disable_constant_velocity()
        braking.append((actor, p["deceleration_ms2"], p.get("target_speed_kmh", 0) / 3.6))
    elif event["type"] == "vehicle_cut_in":
        relative = actor.get_location() - ego_tf.location
        right = ego_tf.get_right_vector()
        side = relative.x * right.x + relative.y * right.y
        if (p["side"] == "left" and side >= 0) or (p["side"] == "right" and side <= 0):
            raise RuntimeError(f"Event {event['id']}: cut-in actor is not on configured {p['side']} side of Ego")
        actor.set_autopilot(False, tm_port)
        actor.disable_constant_velocity()
        duration = float(p.get("lane_change_duration_s", 2))
        forward = ego_tf.get_forward_vector()
        destination = ego_tf.location + forward * (float(p["cut_in_gap_m"]) + ego_speed * duration)
        movements.append({"type": "cut_in", "actor": actor, "end": destination, "until": t + duration,
                          "tm": tm, "speed_kmh": max(5, ego_speed * 3.6)})
    elif event["type"] == "vehicle_crossing":
        actor.disable_constant_velocity()
        destination = resolve_location(p["destination"], f"/events/{event_index}/params/destination", locations or {})
        actor.set_autopilot(True, tm_port)
        tm.set_path(actor, [_transform(carla, destination).location])
        tm.set_desired_speed(actor, float(p["speed_kmh"]))
        tm.auto_lane_change(actor, False)
        tm.ignore_lights_percentage(actor, 100 if p.get("ignore_traffic_lights", False) else 0)
    elif event["type"] == "traffic_light_override":
        candidates = []
        forward = ego_tf.get_forward_vector()
        for light in world.get_actors().filter("traffic.traffic_light*"):
            delta = light.get_location() - ego_tf.location
            if delta.x * forward.x + delta.y * forward.y > 0:
                candidates.append((delta.length(), light))
        if not candidates:
            raise RuntimeError(f"Event {event['id']}: no next traffic light exists ahead of Ego")
        light = min(candidates, key=lambda item: item[0])[1]
        group = light.get_group_traffic_lights()
        for member in group:
            light_overrides.append((member, member.get_state(), member.is_frozen(), t + float(p["duration_s"])))
            member.freeze(True)
        light.set_state(getattr(carla.TrafficLightState, p["state"].capitalize()))
    else:
        raise ValueError(f"Unsupported event: {event['type']}")


def validate_execution_contract(content, sut_type="behavior_agent"):
    if sut_type not in ("behavior_agent", "autopilot"):
        raise ValueError(f"Unsupported Ego controller: {sut_type}")
    events = content.get("events", [])
    actor_types = {}
    for actor in content.get("actors", []):
        actor_id = actor.get("id")
        if not isinstance(actor_id, str) or not actor_id or actor_id in actor_types:
            raise ValueError("Scenario actors require unique non-empty IDs")
        actor_types[actor_id] = actor.get("type")
    event_ids = set()
    dependencies = {}
    for event in events:
        if event["type"] not in SUPPORTED_EVENTS or event["trigger"]["type"] not in SUPPORTED_TRIGGERS:
            raise ValueError(f"Unsupported event/trigger: {event['id']}")
        # SynchronizeAction needs continuously adjusted arrival times. Reject instead of ignoring it.
        if event.get("params", {}).get("sync"):
            raise ValueError(f"Event {event['id']}: synchronized arrival parameters are not supported by this runner")
        if "time_gap_s" in event.get("params", {}):
            raise ValueError(f"Event {event['id']}: time_gap_s control is not supported by this runner")
        event_id = event.get("id")
        if not isinstance(event_id, str) or not event_id or event_id in event_ids:
            raise ValueError("Scenario events require unique non-empty IDs")
        event_ids.add(event_id)
        if event["type"] != "traffic_light_override":
            expected = "pedestrian" if event["type"] == "pedestrian_crossing" else "vehicle"
            if actor_types.get(event.get("actor_ref")) != expected:
                raise ValueError(f"Event {event_id}: actor_ref must identify an existing {expected}")
        trigger = event["trigger"]
        if trigger["type"] in ("ego_distance_to_actor", "time_to_collision") and trigger.get("actor_ref") not in actor_types:
            raise ValueError(f"Event {event_id}: trigger actor_ref does not exist")
        if trigger["type"] == "after_event":
            dependencies[event_id] = trigger.get("event_ref")
    for event_id, previous in dependencies.items():
        if previous not in event_ids:
            raise ValueError(f"Event {event_id}: after_event reference does not exist")
        visited = {event_id}
        while previous is not None:
            if previous in visited:
                raise ValueError(f"Event {event_id}: after_event references form a cycle")
            visited.add(previous)
            previous = dependencies.get(previous)


def fire_due_events(pending, event_times, t, ego, actors, ego_velocity, velocities, dispatch):
    """Fire each action once, recording its start only after it was applied successfully."""
    due = [item for item in pending if trigger_fired(
        item[0]["trigger"], t, ego, actors, item[1], event_times, ego_velocity, velocities)]
    for item in due:
        dispatch(item)
        pending.remove(item)
        event_times[item[0]["id"]] = t
    return len(due)


def _movement_step(carla, item, t, tm_port):
    actor, destination = item["actor"], item["end"]
    remaining = item["until"] - t
    if remaining <= 0:
        actor.set_autopilot(True, tm_port)
        item["tm"].set_desired_speed(actor, item["speed_kmh"])
        return True
    delta = destination - actor.get_location()
    actor.set_target_velocity(carla.Vector3D(delta.x / remaining, delta.y / remaining, 0))
    return False


def _brake_step(carla, item, dt):
    """Giảm tốc đúng deceleration_ms2 bằng cách đặt vận tốc từng tick, rồi giữ target (0 = phanh hẳn)."""
    actor, decel, target = item
    current = actor.get_velocity().length()
    speed = current if current <= target else max(target, current - decel * dt)   # không bao giờ tăng tốc
    if speed <= 0:
        actor.apply_control(carla.VehicleControl(brake=1.0, hand_brake=True))
        actor.set_target_velocity(carla.Vector3D(0, 0, 0))
    else:
        actor.set_target_velocity(actor.get_transform().get_forward_vector() * speed)


def main():
    ap = argparse.ArgumentParser(description="Record asx.telemetry/1 from CARLA for one execution package.")
    ap.add_argument("package", help="execution package JSON saved from the API")
    ap.add_argument("--profile", choices=["cautious", "normal", "aggressive"], default="normal")
    ap.add_argument("--out", default="recordings")
    ap.add_argument("--host", default="localhost")
    ap.add_argument("--port", type=int, default=2000)
    ap.add_argument("--tm-port", type=int, default=8000)
    ap.add_argument("--run-id", help="id simulation run (Flow 2) ghi vào header, để nộp được cho Oracle")
    ap.add_argument("--seed", type=int, help="ghi đè execution.seed của scenario")
    args = ap.parse_args()
    with open(args.package, encoding="utf-8-sig") as f:
        pkg = json.load(f)
    jsonl, video = record(pkg, args.profile, args.out, args.host, args.port, args.tm_port,
                          run_id=args.run_id, seed=args.seed)
    print(f"telemetry={jsonl}")
    if video:
        print(f"video={video}")


# ---------- dùng cho runner của Flow 2 (docs/oracle-api.md) ----------

def infra_failed_telemetry(run_id, reason):
    """Telemetry tối thiểu cho Run không bắt đầu được (nạp map, spawn lỗi...), để Run không treo ở RUNNING."""
    header = {"kind": "header", "format": FORMAT, "run_id": run_id}
    end = {"kind": "end", "t": 0.0, "outcome": "INFRA_FAILED", "reason": reason}
    return "".join(json.dumps(r, separators=(",", ":")) + "\n" for r in (header, end))


def controller_lookahead(plan, distance_m=12.0, step_m=2.0):
    """Keep CARLA 0.9.16 BehaviorAgent's incoming waypoint valid near a short route goal.

    This is controller lookahead only. The scenario's actual goal and its completion
    tolerance remain unchanged; the recorder stops on the original goal condition.
    """
    extended = list(plan)
    if not extended:
        return extended
    waypoint, option = extended[-1]
    for _ in range(max(0, math.ceil(distance_m / step_m))):
        candidates = waypoint.next(step_m)
        if not candidates:
            break
        yaw = waypoint.transform.rotation.yaw
        waypoint = min(candidates, key=lambda candidate: abs((candidate.transform.rotation.yaw - yaw + 180) % 360 - 180))
        extended.append((waypoint, option))
    return extended


def write_progress_snapshot(progress_path, progress):
    """Progress is best effort: a Windows reader must never abort the simulation."""
    temporary = progress_path + ".tmp"
    try:
        with open(temporary, "w", encoding="utf-8") as stream:
            json.dump(progress, stream)
        os.replace(temporary, progress_path)
        return True
    except PermissionError:
        return False


def record(pkg, profile, out_dir, host, port, tm_port, run_id=None, seed=None,
           record_video=True, lite=False, max_sim_s=None, sut_type="behavior_agent", progress_path=None):
    """Chạy scenario trên CARLA. Trả (jsonl_path, mp4_path|None).

    lite=True: no_rendering_mode + bỏ walker nền + cap NPC — tránh crash
    FSkeletalMeshSceneProxy / ACCESS_VIOLATION trên GPU yếu.
    max_sim_s: cắt timeout scenario (smoke/quick test), None = dùng package.
    """
    sys.path.append(os.path.join(os.environ.get("CARLA_ROOT", ""), "PythonAPI", "carla"))
    import carla
    from agents.navigation.behavior_agent import BehaviorAgent
    import numpy as np

    if not pkg["runnable"]:
        raise RuntimeError(f"Package is not runnable: {pkg['issues']}")
    c = pkg["resolvedContent"]
    validate_execution_contract(c, sut_type)
    locs = {l["path"]: l["transform"] for l in pkg["locations"]}
    ex = c["execution"]
    dt = ex["fixed_delta_seconds"]
    seed = ex["seed"] if seed is None else seed   # ở chế độ --api, seed do campaign cấp cho từng lần lặp
    rng = random.Random(seed)
    # no_rendering tắt luôn RGB video (sensor không ra frame hữu ích).
    if lite:
        record_video = False

    client = carla.Client(host, port)
    client.set_timeout(180.0)
    server = client.get_server_version().split("-")[0]
    if server != pkg["map"]["carlaVersion"]:
        raise RuntimeError(f"CARLA server is {server} but the package needs {pkg['map']['carlaVersion']}.")

    # Nạp map. Trên GPU yếu, load_world(TownXX) hay crash FSkeletalMesh — ưu tiên
    # boot CARLA sẵn đúng map rồi soft-reset (không gọi load_world lại).
    map_name = normalize_map_name(pkg["map"]["name"])
    world = client.get_world()
    if lite:
        try:
            pre_s = world.get_settings()
            pre_s.no_rendering_mode = True
            world.apply_settings(pre_s)
            print("  lite: no_rendering enabled")
        except Exception as ex:  # noqa: BLE001
            print(f"  warning: no_rendering failed: {ex}", file=sys.stderr)

    current = ""
    try:
        current = world.get_map().name  # e.g. /Game/Carla/Maps/Town03
    except Exception:  # noqa: BLE001
        current = ""
    try:
        already = normalize_map_name(current) == map_name
    except ValueError:
        already = False
    if already:
        print(f"map already {map_name} — soft reset (skip load_world)")
    elif lite:
        # load_world trên GPU yếu hay FATAL FSkeletalMesh — không gọi, báo INFRA rõ.
        raise RuntimeError(
            f"CARLA is on '{current or '?'}' but scenario needs '{map_name}'. "
            f"Boot CARLA with --boot-map {map_name} (skip load_world). "
            "On this machine load_world often crashes (ACCESS_VIOLATION / skeletal mesh)."
        )
    else:
        print(f"loading map {map_name}...")
        world = client.load_world(map_name)

    original = world.get_settings()
    settings = world.get_settings()
    settings.synchronous_mode = True
    settings.fixed_delta_seconds = dt
    if lite:
        settings.no_rendering_mode = True
    # Apply ngay — trước mọi tick/spawn (tránh render skeletal mesh).
    world.apply_settings(settings)
    tm = client.get_trafficmanager(tm_port)
    actors = []        # mọi actor tự spawn, hủy khi kết thúc
    sensors = []
    walker_ctrls = []
    light_overrides = []
    try:
        world.apply_settings(settings)
        tm.set_synchronous_mode(True)
        tm.set_random_device_seed(seed)
        world.set_pedestrians_seed(seed)
        _clear_world(world)
        for _ in range(30):
            world.tick()
        world_map = world.get_map()
        bps = world.get_blueprint_library()
        spectator = None if lite else world.get_spectator()

        weather = getattr(carla.WeatherParameters, c["weather"].get("preset", "ClearNoon"))
        for key, value in c["weather"].items():
            if key != "preset":
                setattr(weather, key, value)
        world.set_weather(weather)

        # ego
        ego_cfg = c["ego_vehicle"]
        bp = bps.find(ego_cfg["blueprint"])
        bp.set_attribute("role_name", ego_cfg.get("role_name", "hero"))
        if "color" in ego_cfg and bp.has_attribute("color"):
            bp.set_attribute("color", ego_cfg["color"])
        route = c["route"]
        start = resolve_location(route["start"], "/route/start", locs)
        ego = _spawn_vehicle(world, carla, bp, start, label="ego")
        actors.append(ego)
        if spectator is not None:
            # Camera cửa sổ CARLA bám sau ego — bỏ khi lite/no-render
            ex0, ey0, ez0, ep0, eyaw0 = chase_camera(
                ego.get_transform().location.x, ego.get_transform().location.y,
                ego.get_transform().location.z, ego.get_transform().rotation.yaw)
            spectator.set_transform(carla.Transform(
                carla.Location(ex0, ey0, ez0), carla.Rotation(pitch=ep0, yaw=eyaw0)))
            print("ego spawned — watch the CARLA window (camera follows the hero car)")
        else:
            print("ego spawned [lite: no window chase / no_rendering]")
        print("  setup: actors / traffic / route (console quiet until ticks)…", flush=True)

        # Không dùng sensor.camera.rgb: sync mode bắt world.tick() chờ ảnh →
        # RenderOffScreen/GPU này không trả frame nên kẹt t=0 mãi.
        # Video = chụp cửa sổ CARLA sau tick (không nằm trong pipeline sensor).
        video_frames = []
        video_path = None
        frame_stride = max(1, int(round(1.0 / max(dt, 1e-3) / 12.0)))  # ~12 fps
        shot_i = 0
        if record_video and spectator is None:
            print("  warning: lite — skip video", flush=True)
            record_video = False
        elif record_video:
            print("  video: window capture (no RGB sensor)", flush=True)

        # actor của scenario, theo id để event/trigger tra
        named = {}
        for i, a in enumerate(c.get("actors", [])):
            bp = bps.find(a["blueprint"])
            if bp.has_attribute("role_name"):
                bp.set_attribute("role_name", a["id"])
            if bp.has_attribute("is_invincible"):
                bp.set_attribute("is_invincible", "false")
            spawn = resolve_location(a["spawn"], f"/actors/{i}/spawn", locs)
            label = a.get("id", f"actor{i}")
            is_ped = a.get("type") == "pedestrian" or bp.id.startswith("walker.")
            if is_ped and lite:
                raise RuntimeError(f"Lite mode cannot execute required pedestrian actor {label}; use Low quality without --lite")
            if is_ped:
                actor = world.try_spawn_actor(bp, _transform(carla, spawn))
                if actor is None:
                    raise RuntimeError(f"Spawn failed for pedestrian {label}")
                world.tick()
            else:
                actor = _spawn_vehicle(world, carla, bp, spawn, label=label)
            actors.append(actor)
            named[a["id"]] = actor
            if a.get("behavior") == "autopilot":
                actor.set_autopilot(True, tm_port)

        # traffic nền: chọn spawn point bằng rng có seed, tránh chỗ ego và actor scenario đang đứng
        npc = c.get("traffic", {}).get("npc_vehicles", {})
        npc_count = int(npc.get("count", 0) or 0)
        if lite:
            npc_count = min(npc_count, 3)
        occupied = [ego.get_location()] + [a.get_location() for a in named.values()]
        points = [p for p in world_map.get_spawn_points() if all(p.location.distance(o) > 10 for o in occupied)]
        rng.shuffle(points)
        vbps = [b for b in bps.filter(npc.get("blueprint_filter", "vehicle.*"))
                if not npc.get("four_wheels_only", True) or int(b.get_attribute("number_of_wheels")) == 4]
        vbps.sort(key=lambda b: b.id)
        batch = []
        if npc_count and (not vbps or len(points) < npc_count):
            raise RuntimeError(f"Requested {npc_count} background vehicles, but only {len(points)} free spawn points exist")
        for p in points[:npc_count]:
            b = rng.choice(vbps)
            b.set_attribute("role_name", "autopilot")
            batch.append(carla.command.SpawnActor(b, p).then(
                carla.command.SetAutopilot(carla.command.FutureActor, True, tm_port)))
        results = client.apply_batch_sync(batch, True)
        actors += [world.get_actor(r.actor_id) for r in results if not r.error]
        if any(result.error for result in results):
            raise RuntimeError("Background vehicle spawn failed; configured traffic was not fully created")
        print(f"  traffic: {npc_count} NPC vehicles", flush=True)
        tm.global_percentage_speed_difference(npc.get("speed_difference_percent", 0))
        tm.set_global_distance_to_leading_vehicle(npc.get("distance_to_leading_m", 2.5))

        peds = c.get("traffic", {}).get("pedestrians", {})
        ped_count = 0 if lite else int(peds.get("count", 0) or 0)
        world.set_pedestrians_cross_factor(0 if lite else peds.get("crossing_probability", 0))
        wbps = sorted(bps.filter("walker.pedestrian.*"), key=lambda b: b.id)
        walkers = []
        for _ in range(ped_count):
            loc = world.get_random_location_from_navigation()
            b = rng.choice(wbps)
            if b.has_attribute("is_invincible"):
                b.set_attribute("is_invincible", "false")
            w = world.try_spawn_actor(b, carla.Transform(loc)) if loc else None
            if w:
                actors.append(w)
                walkers.append(w)
        world.tick()
        if len(walkers) != ped_count:
            raise RuntimeError(f"Requested {ped_count} background pedestrians, spawned only {len(walkers)}")
        if walkers:
            ctrl_bp = bps.find("controller.ai.walker")
            for w in walkers:
                ctrl = world.spawn_actor(ctrl_bp, carla.Transform(), attach_to=w)
                walker_ctrls.append(ctrl)
            world.tick()
            for ctrl in walker_ctrls:
                ctrl.start()
                ctrl.go_to_location(world.get_random_location_from_navigation())
                ctrl.set_max_speed(3.0 if rng.random() < peds.get("running_probability", 0) else 1.4)

        # SUT: kế hoạch đi qua start -> waypoints -> goal
        agent = BehaviorAgent(ego, behavior=profile) if sut_type == "behavior_agent" else None
        from agents.navigation.global_route_planner import GlobalRoutePlanner
        planner = GlobalRoutePlanner(world_map, sampling_resolution=2.0)
        stops = [start] + [resolve_location(w, f"/route/waypoints/{i}", locs) for i, w in enumerate(route.get("waypoints", []))]
        goal = resolve_location(route["goal"], "/route/goal", locs)
        stops.append(goal)
        plan = []
        for src, dst in zip(stops, stops[1:]):
            plan += planner.trace_route(_transform(carla, src).location, _transform(carla, dst).location)
        if len(plan) < 2:
            raise RuntimeError("Route planner returned no drivable route")
        if agent is not None:
            agent.set_global_plan(controller_lookahead(plan))
            agent.set_target_speed(float(route.get("speed_limit_kmh", 20)))
        else:
            tm.set_path(ego, [point.transform.location for point, _ in plan])
            tm.set_desired_speed(ego, float(route.get("speed_limit_kmh", 20)))
            tm.auto_lane_change(ego, False)
        goal_loc = _transform(carla, goal).location

        events = []
        frame0 = world.tick()
        print("  first tick ok", flush=True)

        def at(frame):
            return _r((frame - frame0) * dt)

        def on_collision(e):
            imp = e.normal_impulse
            tf = ego.get_transform()
            fwd, right = tf.get_forward_vector(), tf.get_right_vector()
            other = e.other_actor   # có thể None khi đâm vào hình học tĩnh của map
            events.append({"t": at(e.frame), "type": "collision",
                           "other_actor": {"id": other.id if other else 0, "type": _type_of(other.type_id) if other else "static"},
                           "impulse": _r(imp.length()),
                           "impact_side": impact_side((fwd.x, fwd.y), (right.x, right.y), (imp.x, imp.y))})

        def on_lane(e):
            events.append({"t": at(e.frame), "type": "lane_invasion",
                           "markings": [str(m.type) for m in e.crossed_lane_markings]})

        for name, cb in (("sensor.other.collision", on_collision), ("sensor.other.lane_invasion", on_lane)):
            s = world.spawn_actor(bps.find(name), carla.Transform(), attach_to=ego)
            s.listen(cb)
            sensors.append(s)

        pending = []
        for i, e in enumerate(c.get("events", [])):
            if e["type"] in SUPPORTED_EVENTS:
                where = None
                if e["trigger"]["type"] == "ego_distance_to_location":
                    where = resolve_location(e["trigger"]["location"], f"/events/{i}/trigger/location", locs)
                pending.append((e, where, i))
            else:
                raise RuntimeError(f"Unsupported event {e['id']} ({e['type']})")
        braking = []   # (actor, deceleration, target m/s)
        movements, event_times = [], {}

        extent = ego.bounding_box.extent
        header = {"run_id": run_id or str(uuid.uuid4()), "scenario_version_id": pkg["scenarioVersionId"],
                  "content_hash": pkg["contentHash"], "carla_version": server,
                  "sut": {"agent": sut_type, "profile": profile}, "seed": seed,
                  "scripted_actors": {ref: actor.id for ref, actor in named.items()},
                  "fixed_delta_seconds": dt, "ego_extent": {"x": _r(extent.x), "y": _r(extent.y)},
                  # Oracle đọc: Route completion (goal), Freezing (warmup_s), Speeding (giới hạn cố định của route nếu có)
                  "goal": {"x": _r(goal["x"]), "y": _r(goal["y"])}, "warmup_s": ex.get("warmup_s", 2)}
        if "speed_limit_kmh" in route:
            header["route_speed_limit_kmh"] = route["speed_limit_kmh"]
        os.makedirs(out_dir, exist_ok=True)
        path = os.path.join(out_dir, f"{pkg['scenarioCode']}_v{pkg['versionNumber']}_{profile}_{header['run_id'][:8]}.jsonl")
        print(f"recording {path}")

        warmup, timeout = ex.get("warmup_s", 2), float(ex["timeout_s"])
        package_timeout = timeout
        smoke_cap = max_sim_s is not None and max_sim_s > 0 and float(max_sim_s) < package_timeout
        if smoke_cap:
            timeout = float(max_sim_s)
        tolerance = route.get("goal_tolerance_m", 5)
        # ADR 0001: dừng sớm khi va chạm, hoặc khi vượt đèn (chạy tiếp qua vạch lúc đèn đỏ).
        # Phanh và đứng chờ đèn, kể cả trôi qua vạch một đoạn ngắn, không phải vượt đèn.
        extent_x = float(extent.x)
        with TelemetryFile(path, header) as out:
            frame, stop_at, started = frame0, None, False
            violation_label = None
            prev_front = None
            pending_red = None
            last_heartbeat = -10.0
            print(f"  sim start timeout_s={timeout} warmup_s={warmup}", flush=True)
            while True:
                t = at(frame)
                if t - last_heartbeat >= 5.0:
                    print(f"  … t={t:.1f}s frames={len(video_frames) if record_video else 0}", flush=True)
                    last_heartbeat = t
                # shortcut: callback sensor chạy ở thread khác, event có thể ghi sau tick cùng frame; Oracle không đòi thứ tự chặt.
                while events:
                    e = events.pop(0)
                    out.event({**e, "post_stop": stop_at is not None and e["t"] > stop_at})
                    if e["type"] in ("collision", "red_light") and stop_at is None:
                        stop_at = e["t"]
                        violation_label = "COLLISION" if e["type"] == "collision" else "RED LIGHT"
                        _focus_carla(_find_carla_hwnd())
                        print(
                            f"  violation {e['type']} at t={stop_at:.1f}s — recording {POST_STOP_S:.0f}s more",
                            flush=True,
                        )
                post_stop = stop_at is not None and t > stop_at

                tf, v, acc, ctl = ego.get_transform(), ego.get_velocity(), ego.get_acceleration(), ego.get_control()
                if progress_path and frame % max(1, int(1 / dt)) == 0:
                    progress = {"simulationTimeS": t, "frame": frame, "egoX": tf.location.x,
                                "egoY": tf.location.y, "egoZ": tf.location.z,
                                "speedKmh": math.hypot(v.x, v.y) * 3.6, "message": f"Running {map_name}"}
                    write_progress_snapshot(progress_path, progress)
                road, lane = _lane(world_map, tf.location, carla)
                front = front_bumper(tf.location.x, tf.location.y, tf.rotation.yaw, extent_x)
                light = str(ego.get_traffic_light_state()).lower() if ego.is_at_traffic_light() else "none"
                speed_ms = math.hypot(v.x, v.y)
                if stop_at is None and t >= warmup:
                    if pending_red is None and light == "red" and prev_front is not None:
                        tl = ego.get_traffic_light()
                        lines = _stop_lines(tl, tf.location, tf.rotation.yaw) if tl is not None else []
                        if crossed_stop_line(prev_front, front, lines):
                            pending_red = (tl.id if tl else 0, tf.location.x, tf.location.y, False)
                            print(
                                f"  reached a red stop line at t={t:.1f}s — waiting to see if the car stops",
                                flush=True,
                            )
                    elif pending_red is not None:
                        light_id, x0, y0, was_stopped = pending_red
                        judged, ran = judge_red_light(
                            (x0, y0, was_stopped), light == "red", speed_ms, tf.location.x, tf.location.y)
                        if ran:
                            out.event({"t": t, "post_stop": False, "type": "red_light", "light_id": light_id})
                            stop_at = t
                            violation_label = "RED LIGHT"
                            pending_red = None
                            _focus_carla(_find_carla_hwnd())
                            print(
                                f"  violation red_light at t={t:.1f}s — drove through on red — recording {POST_STOP_S:.0f}s more",
                                flush=True,
                            )
                        elif judged is None:
                            if was_stopped:
                                print(f"  waited out the red light at t={t:.1f}s — not a violation", flush=True)
                            pending_red = None
                        else:
                            if judged[2] and not was_stopped:
                                print(f"  stopped for the red light at t={t:.1f}s — not a violation", flush=True)
                            pending_red = (light_id, judged[0], judged[1], judged[2])
                prev_front = front
                if spectator is not None:
                    # Giữ camera sau xe cả khi có lỗi, để video thấy xe và ngã tư phía trước.
                    cx, cy, cz, cpitch, cyaw = chase_camera(
                        tf.location.x, tf.location.y, tf.location.z, tf.rotation.yaw)
                    spectator.set_transform(carla.Transform(
                        carla.Location(cx, cy, cz), carla.Rotation(pitch=cpitch, yaw=cyaw)))
                nearby = []
                for a in world.get_actors():
                    if a.id == ego.id or not _tracked(a):
                        continue
                    loc = a.get_location()
                    if loc.distance(tf.location) > ACTOR_RADIUS_M:
                        continue
                    av, ae = a.get_velocity(), a.bounding_box.extent
                    a_road, a_lane = _lane(world_map, loc, carla)
                    nearby.append({"id": a.id, "type": _type_of(a.type_id), "x": _r(loc.x), "y": _r(loc.y),
                                   "yaw": _r(a.get_transform().rotation.yaw), "vx": _r(av.x), "vy": _r(av.y),
                                   "road_id": a_road, "lane_id": a_lane, "extent": {"x": _r(ae.x), "y": _r(ae.y)}})
                out.tick({"t": t, "post_stop": post_stop,
                          "ego": {"x": _r(tf.location.x), "y": _r(tf.location.y), "z": _r(tf.location.z),
                                  "yaw": _r(tf.rotation.yaw), "pitch": _r(tf.rotation.pitch), "roll": _r(tf.rotation.roll),
                                  "vx": _r(v.x), "vy": _r(v.y), "ax": _r(acc.x), "ay": _r(acc.y),
                                  "throttle": _r(ctl.throttle), "brake": _r(ctl.brake), "steer": _r(ctl.steer),
                                  "speed_limit_kmh": _r(ego.get_speed_limit()), "road_id": road, "lane_id": lane},
                          "traffic_light": light if light in LIGHT_STATES else "none",
                          "actors": nearby})

                if smoke_cap and t >= float(max_sim_s):
                    # QUICK: luôn quay đủ 40s, kể cả sau đèn đỏ / va chạm.
                    outcome = "STOPPED_ON_VIOLATION" if stop_at is not None else "TRUNCATED"
                elif not smoke_cap and stop_at is not None and t >= stop_at + POST_STOP_S:
                    outcome = "STOPPED_ON_VIOLATION"
                elif not smoke_cap and t >= warmup + timeout:
                    outcome = "TIMED_OUT"
                elif not smoke_cap and started and tf.location.distance(goal_loc) <= tolerance:
                    outcome = "COMPLETED"
                else:
                    outcome = None
                if outcome:
                    if outcome == "COMPLETED" and pending:
                        out.finish("INFRA_FAILED", "Required scenario events did not trigger: " + ", ".join(p[0]["id"] for p in pending))
                    else:
                        out.finish(outcome)
                    break

                if t < warmup:
                    ego.apply_control(carla.VehicleControl(brake=1.0, hand_brake=True))
                else:
                    if not started:
                        started = True
                        _start_moving(carla, ego, ego_cfg.get("initial_speed_kmh", 0), c.get("actors", []), named)
                        if agent is None:
                            ego.set_autopilot(True, tm_port)
                    ego_pos = (tf.location.x, tf.location.y, tf.location.z)
                    positions = {ref: (l.x, l.y, l.z) for ref, l in ((ref, a.get_location()) for ref, a in named.items())}
                    velocities = {ref: tuple(getattr(a.get_velocity(), axis) for axis in ("x", "y", "z")) for ref, a in named.items()}
                    def dispatch_event(item):
                        _fire(carla, item[0], named.get(item[0].get("actor_ref")), tf, tm_port, braking,
                              tm=tm, world=world, locations=locs, event_index=item[2], movements=movements,
                              light_overrides=light_overrides, t=t, ego_speed=speed_ms)
                        out.event({"t": t, "post_stop": post_stop, "type": "scenario_action",
                                   "event_id": item[0]["id"], "action": item[0]["type"],
                                   "actor_ref": item[0].get("actor_ref")})
                    fire_due_events(pending, event_times, t - warmup, ego_pos, positions, (v.x, v.y, v.z), velocities, dispatch_event)
                    for movement in list(movements):
                        if _movement_step(carla, movement, t, tm_port):
                            movements.remove(movement)
                    for override in list(light_overrides):
                        light_actor, state, frozen, until = override
                        if t >= until:
                            light_actor.set_state(state)
                            light_actor.freeze(frozen)
                            light_overrides.remove(override)
                    for b in braking:
                        _brake_step(carla, b, dt)
                    if agent is not None:
                        ego.apply_control(agent.run_step())
                frame = world.tick()
                if record_video and len(video_frames) < 900:
                    shot_i += 1
                    if shot_i % frame_stride == 0:
                        fr = _grab_carla_window()
                        if fr is not None:
                            if violation_label:
                                spd = math.hypot(v.x, v.y) * 3.6
                                fr = _stamp_evidence(fr, f"{violation_label}  {spd:.0f} km/h")
                            video_frames.append(fr)
        if record_video and video_frames:
            video_path = os.path.splitext(path)[0] + ".mp4"
            video_path = write_mp4(video_path, video_frames, fps=12)
            if video_path:
                print(f"video {video_path} ({len(video_frames)} frames)")
        return path, video_path
    finally:
        for light_actor, state, frozen, _ in light_overrides:
            try:
                light_actor.set_state(state)
                light_actor.freeze(frozen)
            except Exception:
                pass
        for s in sensors:
            try:
                s.stop()
            except Exception:
                pass
        for ctrl in walker_ctrls:
            try:
                ctrl.stop()
            except Exception:
                pass
        try:
            client.apply_batch_sync([carla.command.DestroyActor(a) for a in sensors + walker_ctrls + actors if a])
        except Exception:
            pass
        try:
            tm.set_synchronous_mode(False)
            world.apply_settings(original)
        except Exception:
            pass


if __name__ == "__main__":
    main()
