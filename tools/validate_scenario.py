#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Validator 3 tầng cho scenario schema v1.0 - BẢN THAM CHIẾU.

Đây là bản Python độc lập của cùng bộ luật mà backend C# cài đặt trong
backend/AutoScenarioX.Api/Validators/ScenarioContent/. Mục đích: chạy cùng một file
qua cả hai bản và đối chiếu kết quả, để chứng minh bản C# không sót hay
dịch sai luật nào.

    Tầng 1 (schema)   JSON Schema draft 2020-12, chạy trên nội dung NGUYÊN VĂN
    Tầng 2 (business) 6 luật nghiệp vụ, chạy trên nội dung ĐÃ ĐIỀN DEFAULT
    Tầng 3 (carla)    đối chiếu map metadata xuất từ CARLA: map, spawn point, NPC,
                      vị trí trên map, chồng spawn, route chạy được, trigger tại điểm xuất phát

Tầng sau chỉ chạy khi tầng trước sạch lỗi. Warning KHÔNG làm hỏng kết quả.

Cách dùng:
    python tools/validate_scenario.py examples/S23_pedestrian_crossing_rain.json
    python tools/validate_scenario.py examples/S23_pedestrian_crossing_rain.json --metadata-dir map_metadata/
    python tools/validate_scenario.py examples/*.json --json

Mã thoát: 0 nếu mọi file đều hợp lệ, 1 nếu có file không hợp lệ, 2 nếu lỗi sử dụng.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys

try:
    from jsonschema import Draft202012Validator
except ImportError:  # pragma: no cover
    sys.stderr.write("Thiếu thư viện: pip install jsonschema pyyaml\n")
    raise SystemExit(2)

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SCHEMA = os.path.join(
    REPO_ROOT, "backend", "AutoScenarioX.Api", "Resources", "scenario.schema.json"
)

PARAM_REF_KEY = "$param"

TIER_SCHEMA = "schema"
TIER_BUSINESS = "business"
TIER_CARLA = "carla"
TIER_ORDER = (TIER_SCHEMA, TIER_BUSINESS, TIER_CARLA)

# Từ khoá tạo NHÁNH trong schema. Nhánh không khớp vẫn bị báo lỗi dù tài liệu
# đúng (vd location dùng spawn_point_index thì nhánh toạ độ luôn "thiếu x, y"),
# nên những lỗi đó chỉ được dùng khi không còn lỗi trực tiếp nào.
# Khớp SchemaValidationRule.BranchKeywords bên C#.
BRANCH_KEYWORDS = ("anyOf", "oneOf", "if", "not")

# Loại actor bắt buộc cho từng event type; traffic_light_override không dùng actor.
# Khớp ActorReferenceRule.RequiredActorType bên C#.
REQUIRED_ACTOR_TYPE = {
    "pedestrian_crossing": "pedestrian",
    "vehicle_cut_in": "vehicle",
    "lead_vehicle_brake": "vehicle",
    "vehicle_crossing": "vehicle",
}

# Các khối được đưa vào content_hash. metadata / source / extensions KHÔNG được tính
# -> đổi mô tả hay tag không sinh version mới.
# Khớp CanonicalJsonHasher.HashedBlocks bên C#.
HASHED_BLOCKS = (
    "map", "route", "weather", "ego_vehicle", "traffic",
    "actors", "events", "parameters", "execution",
)


# --------------------------------------------------------------------------
# Kiểu dữ liệu kết quả
# --------------------------------------------------------------------------
class Issue(object):
    """Một phát hiện của validator. path là JSON Pointer (vd "/route/goal")."""

    __slots__ = ("tier", "severity", "path", "message")

    def __init__(self, tier, severity, path, message):
        self.tier = tier
        self.severity = severity
        self.path = path
        self.message = message

    def as_dict(self):
        return {"tier": self.tier, "path": self.path, "message": self.message}

    def __repr__(self):
        return "<%s %s %s: %s>" % (self.tier, self.severity, self.path, self.message)


def error(tier, path, message):
    return Issue(tier, "error", path, message)


def warning(tier, path, message):
    return Issue(tier, "warning", path, message)


# --------------------------------------------------------------------------
# Tiện ích đọc nội dung - khớp ScenarioContentReader bên C#
# --------------------------------------------------------------------------
def escape_token(token):
    """Escape một token JSON Pointer theo RFC 6901."""
    return token.replace("~", "~0").replace("/", "~1")


def pointer(parts):
    return "/" + "/".join(escape_token(str(p)) for p in parts) if parts else "/"


def as_string(node):
    return node if isinstance(node, str) else None


def as_param_ref(node):
    """Tên tham số nếu node là tham chiếu {"$param": "..."}; ngược lại None."""
    if isinstance(node, dict) and len(node) == 1 and PARAM_REF_KEY in node:
        return as_string(node[PARAM_REF_KEY])
    return None


def try_get_number(node, parameters):
    """Đọc một trường số, tự giải {"$param": "..."} bằng value của tham số.

    Trả (False, 0) khi thiếu, sai kiểu, hoặc tham số chưa khai báo.
    """
    name = as_param_ref(node)
    if name is not None:
        definition = parameters.get(name)
        if not isinstance(definition, dict):
            return False, 0
        return try_get_number(definition.get("value"), {})

    # bool là subclass của int trong Python nhưng KHÔNG phải số ở JSON
    if isinstance(node, bool) or not isinstance(node, (int, float)):
        return False, 0
    return True, float(node)


def fmt_num(value):
    """In số giống C# double.ToString(): 15.0 -> "15", 2.5 -> "2.5"."""
    if value == int(value):
        return str(int(value))
    return repr(value)


def get_parameters(root):
    value = root.get("parameters")
    return value if isinstance(value, dict) else {}


def get_actors(root):
    value = root.get("actors")
    return value if isinstance(value, list) else []


def get_events(root):
    value = root.get("events")
    return value if isinstance(value, list) else []


def actor_types_by_id(root):
    """Map id -> type của các actor khai báo tường minh."""
    result = {}
    for actor in get_actors(root):
        if not isinstance(actor, dict):
            continue
        actor_id = as_string(actor.get("id"))
        if actor_id is None:
            continue
        result[actor_id] = as_string(actor.get("type")) or ""
    return result


def walk_objects(node, parts=()):
    """Duyệt toàn bộ cây, trả (JSON Pointer, dict) của từng object."""
    if isinstance(node, dict):
        yield pointer(parts), node
        for key, value in node.items():
            for found in walk_objects(value, tuple(parts) + (key,)):
                yield found
    elif isinstance(node, list):
        for index, value in enumerate(node):
            for found in walk_objects(value, tuple(parts) + (index,)):
                yield found


def canonical(node):
    """Dạng chuẩn hoá để so sánh hai nhánh không phụ thuộc thứ tự key."""
    return json.dumps(node, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def are_equivalent(left, right):
    return canonical(left) == canonical(right)


# --------------------------------------------------------------------------
# Chuẩn hoá - khớp ScenarioContentNormalizer bên C#
# --------------------------------------------------------------------------
def set_default(target, key, value):
    if isinstance(target, dict) and key not in target:
        target[key] = value


def normalize_location(node):
    """Toạ độ thế giới ({x, y}) có default z = 0.5; spawn_point_index không có."""
    if isinstance(node, dict) and "x" in node:
        set_default(node, "z", 0.5)


def normalize(root):
    """Điền các giá trị "default" khai báo trong schema.

    CỐ TÌNH khoan dung: gặp kiểu sai thì bỏ qua, không ném lỗi - sai kiểu là
    việc của tầng 1.
    """
    if not isinstance(root, dict):
        return {}
    root = json.loads(json.dumps(root))  # deep copy, không đụng bản gốc

    set_default(root, "actors", [])
    set_default(root, "events", [])
    set_default(root, "parameters", {})

    route = root.get("route")
    if isinstance(route, dict):
        set_default(route, "goal_tolerance_m", 5)
        normalize_location(route.get("start"))
        normalize_location(route.get("goal"))
        if isinstance(route.get("waypoints"), list):
            for waypoint in route["waypoints"]:
                normalize_location(waypoint)

    weather = root.get("weather")
    if isinstance(weather, dict):
        set_default(weather, "preset", "ClearNoon")

    ego = root.get("ego_vehicle")
    if isinstance(ego, dict):
        set_default(ego, "blueprint", "vehicle.tesla.model3")
        set_default(ego, "role_name", "hero")
        set_default(ego, "initial_speed_kmh", 0)

    traffic = root.get("traffic")
    if isinstance(traffic, dict):
        npc = traffic.get("npc_vehicles")
        if isinstance(npc, dict):
            set_default(npc, "count", 0)
            set_default(npc, "blueprint_filter", "vehicle.*")
            set_default(npc, "four_wheels_only", True)
            set_default(npc, "speed_difference_percent", 0)
            set_default(npc, "distance_to_leading_m", 2.5)
        pedestrians = traffic.get("pedestrians")
        if isinstance(pedestrians, dict):
            set_default(pedestrians, "count", 0)
            set_default(pedestrians, "crossing_probability", 0)
            set_default(pedestrians, "running_probability", 0)

    execution = root.get("execution")
    if isinstance(execution, dict):
        set_default(execution, "fixed_delta_seconds", 0.05)
        set_default(execution, "synchronous_mode", True)
        set_default(execution, "warmup_s", 2)

    for actor in get_actors(root):
        if not isinstance(actor, dict):
            continue
        set_default(actor, "initial_speed_kmh", 0)
        set_default(actor, "behavior", "idle")
        normalize_location(actor.get("spawn"))

    for event in get_events(root):
        if not isinstance(event, dict):
            continue
        trigger = event.get("trigger")
        if isinstance(trigger, dict):
            normalize_location(trigger.get("location"))
        params = event.get("params")
        if not isinstance(params, dict):
            continue
        event_type = event.get("type")
        if event_type in ("pedestrian_crossing", "vehicle_crossing"):
            sync = params.get("sync")
            if isinstance(sync, dict):
                set_default(sync, "offset_s", 0)
                normalize_location(sync.get("point"))
        if event_type == "pedestrian_crossing":
            set_default(params, "direction", "left_to_right")
        elif event_type == "vehicle_cut_in":
            set_default(params, "lane_change_duration_s", 2)
        elif event_type == "lead_vehicle_brake":
            set_default(params, "target_speed_kmh", 0)
        elif event_type == "vehicle_crossing":
            set_default(params, "ignore_traffic_lights", False)
            normalize_location(params.get("destination"))
        elif event_type == "traffic_light_override":
            set_default(params, "target", "next_on_route")

    return root


# --------------------------------------------------------------------------
# content_hash - khớp CanonicalJsonHasher + CanonicalJsonSerializer bên C#
# --------------------------------------------------------------------------
def compute_content_hash(normalized):
    """SHA-256 của JSON chuẩn hoá, CHỈ gồm 9 khối ảnh hưởng tới thực thi.

    Chuẩn hoá = key sắp theo thứ tự ordinal, không khoảng thừa, UTF-8 giữ nguyên.
    Trả "sha256:<64 hex>" (khớp CHECK trong DB).
    """
    subset = {}
    for block in HASHED_BLOCKS:
        if block in normalized and normalized[block] is not None:
            subset[block] = normalized[block]
    digest = hashlib.sha256(canonical(subset).encode("utf-8")).hexdigest()
    return "sha256:" + digest


# --------------------------------------------------------------------------
# Tầng 1 - JSON Schema
# --------------------------------------------------------------------------
def instance_pointer(err):
    return pointer(tuple(err.absolute_path))


def is_inside_branch(err):
    return any(str(part) in BRANCH_KEYWORDS for part in err.absolute_schema_path)


def validate_schema_tier(raw, schema):
    """Tầng 1 chạy trên nội dung NGUYÊN VĂN để lỗi chỉ đúng chỗ người dùng nhập."""
    validator = Draft202012Validator(schema)
    failures = list(validator.iter_errors(raw))
    if not failures:
        return []

    # Ưu tiên lỗi trực tiếp; chỉ khi không có mới tóm tắt lỗi trong nhánh.
    direct = [e for e in failures if e.validator not in BRANCH_KEYWORDS and not is_inside_branch(e)]
    if direct:
        seen = set()
        issues = []
        for err in direct:
            key = (instance_pointer(err), err.message)
            if key in seen:
                continue
            seen.add(key)
            issues.append(error(TIER_SCHEMA, key[0], err.message))
        return issues

    paths = sorted({instance_pointer(e) for e in failures}, key=len)
    if paths:
        return [
            error(
                TIER_SCHEMA,
                paths[0],
                "The value does not match any type allowed by the schema at this location.",
            )
        ]
    return [error(TIER_SCHEMA, "/", "Content does not match scenario schema v1.0.")]


# --------------------------------------------------------------------------
# Tầng 2 - luật nghiệp vụ (7 luật, khớp Rules/Business/*.cs)
# --------------------------------------------------------------------------
def rule_parameter_range(root):
    """Giá trị hiện tại của mỗi tham số phải nằm trong [min, max] và min <= max."""
    issues = []
    for name, definition in get_parameters(root).items():
        if not isinstance(definition, dict):
            continue
        path = "/parameters/%s" % escape_token(name)

        ok_value, value = try_get_number(definition.get("value"), {})
        ok_min, minimum = try_get_number(definition.get("min"), {})
        ok_max, maximum = try_get_number(definition.get("max"), {})
        if not (ok_value and ok_min and ok_max):
            continue  # thiếu / sai kiểu đã bị tầng 1 bắt

        if minimum > maximum:
            issues.append(error(
                TIER_BUSINESS, path,
                "Parameter '%s' has a minimum (%s) greater than its maximum (%s)."
                % (name, fmt_num(minimum), fmt_num(maximum))))
            continue

        if value < minimum or value > maximum:
            issues.append(error(
                TIER_BUSINESS, "%s/value" % path,
                "Parameter '%s' has value %s, outside the range [%s, %s]."
                % (name, fmt_num(value), fmt_num(minimum), fmt_num(maximum))))
    return issues


def rule_param_reference(root):
    """Mọi {"$param": "ten"} phải trỏ tới một tham số đã khai báo."""
    issues = []
    parameters = get_parameters(root)
    for path, node in walk_objects(root):
        name = as_param_ref(node)
        if name is None or name in parameters:
            continue
        issues.append(error(
            TIER_BUSINESS, path,
            "Parameter reference '%s' is not defined in parameters." % name))
    return issues


PARAM_REF_POINTER = "#/$defs/paramRef"


def _resolve(schema, defs):
    while isinstance(schema, dict) and isinstance(schema.get("$ref"), str):
        schema = defs.get(schema["$ref"].split("/")[-1])
    return schema if isinstance(schema, dict) else None


def _is_param_ref_branch(branch):
    return isinstance(branch, dict) and branch.get("$ref") == PARAM_REF_POINTER


def _choose_branch(branches, content):
    """Chọn nhánh oneOf của object: khớp "type" const, nếu không thì nhánh có đủ trường bắt buộc."""
    candidates = [b for b in branches if isinstance(b, dict)]
    kind = as_string(content.get("type"))
    for branch in candidates:
        const = ((branch.get("properties") or {}).get("type") or {}).get("const")
        if kind is not None and const == kind:
            return branch
    for branch in candidates:
        if all(isinstance(k, str) and k in content for k in branch.get("required") or []):
            return branch
    return None


def _merged_properties(schema, content, defs):
    """properties của object, cộng thêm then.properties của các nhánh allOf/if khớp (ghi đè)."""
    result = {k: v for k, v in (schema.get("properties") or {}).items() if isinstance(v, dict)}
    for branch in schema.get("allOf") or []:
        conditions = ((branch or {}).get("if") or {}).get("properties")
        if not isinstance(conditions, dict):
            continue

        def matches(key, rule):
            actual = as_string(content.get(key))
            if actual is None or not isinstance(rule, dict):
                return False
            if "const" in rule:
                return rule["const"] == actual
            return isinstance(rule.get("enum"), list) and actual in rule["enum"]

        if all(matches(k, r) for k, r in conditions.items()):
            then = _resolve(branch.get("then"), defs) or {}
            for k, v in (then.get("properties") or {}).items():
                if isinstance(v, dict):
                    result[k] = v
    return result


def _check_reference(name, numeric, path, parameters, issues):
    definition = parameters.get(name)
    if not isinstance(definition, dict):
        return  # tham số chưa khai báo đã do rule_param_reference báo
    numbers = [try_get_number(definition.get(k), {}) for k in ("value", "min", "max")]
    numbers += [try_get_number(numeric.get(k), {}) for k in ("minimum", "maximum")]
    if not all(ok for ok, _ in numbers):
        return
    value, minimum, maximum, low, high = (n for _, n in numbers)

    if numeric.get("type") == "integer" and (
            definition.get("type") != "integer" or value != int(value)):
        issues.append(error(
            TIER_BUSINESS, path,
            "Parameter '%s' is used by an integer field and must have type 'integer' "
            "with a whole value." % name))
    elif value < low or value > high:
        issues.append(error(
            TIER_BUSINESS, path,
            "Parameter '%s' has value %s, but this field accepts %s to %s."
            % (name, fmt_num(value), fmt_num(low), fmt_num(high))))
    elif minimum < low or maximum > high:
        issues.append(error(
            TIER_BUSINESS, path,
            "Parameter '%s' allows %s to %s, but this field accepts %s to %s."
            % (name, fmt_num(minimum), fmt_num(maximum), fmt_num(low), fmt_num(high))))


def rule_param_target_range(root, schema):
    """Tham số gắn bằng {"$param"} phải nằm trong miền schema cho phép ở trường đó.

    Duyệt nội dung song song với schema; miền lấy từ nhánh số của oneOf tại chỗ dùng $param.
    """
    defs = schema.get("$defs") or {}
    parameters = get_parameters(root)
    issues = []

    def check(node_schema, content, parts):
        node_schema = _resolve(node_schema, defs)
        if node_schema is None or content is None:
            return
        branches = node_schema.get("oneOf")
        if isinstance(branches, list):
            if any(_is_param_ref_branch(b) for b in branches):
                name = as_param_ref(content)
                numeric = next((_resolve(b, defs) for b in branches
                                if not _is_param_ref_branch(b)), None)
                if name is not None and numeric is not None:
                    _check_reference(name, numeric, pointer(parts), parameters, issues)
                return
            if isinstance(content, dict):
                chosen = _choose_branch([_resolve(b, defs) for b in branches], content)
                if chosen is not None:
                    check(chosen, content, parts)
            return
        if isinstance(content, dict):
            properties = _merged_properties(node_schema, content, defs)
            extra = node_schema.get("additionalProperties")
            for key, value in content.items():
                child = properties.get(key)
                if child is None and isinstance(extra, dict):
                    child = extra
                check(child, value, tuple(parts) + (key,))
        elif isinstance(content, list) and isinstance(node_schema.get("items"), dict):
            for index, value in enumerate(content):
                check(node_schema["items"], value, tuple(parts) + (index,))

    check(schema, root, ())
    return issues


def rule_actor_reference(root):
    """actor_ref của event và trigger phải trỏ tới actor có thật và đúng loại."""
    issues = []
    actor_types = actor_types_by_id(root)
    events = get_events(root)

    for index, event in enumerate(events):
        if not isinstance(event, dict):
            continue
        event_type = as_string(event.get("type"))

        actor_ref = as_string(event.get("actor_ref"))
        if actor_ref is not None:
            path = "/events/%d/actor_ref" % index
            if actor_ref not in actor_types:
                issues.append(error(
                    TIER_BUSINESS, path,
                    "The event references actor '%s', which is not defined in actors." % actor_ref))
            else:
                required = REQUIRED_ACTOR_TYPE.get(event_type) if event_type else None
                actor_type = actor_types[actor_ref]
                if required is not None and actor_type != required:
                    issues.append(error(
                        TIER_BUSINESS, path,
                        "Event '%s' requires an actor of type '%s', but '%s' is '%s'."
                        % (event_type, required, actor_ref, actor_type)))

        trigger = event.get("trigger")
        if isinstance(trigger, dict):
            trigger_ref = as_string(trigger.get("actor_ref"))
            if trigger_ref is not None and trigger_ref not in actor_types:
                issues.append(error(
                    TIER_BUSINESS, "/events/%d/trigger/actor_ref" % index,
                    "The trigger references actor '%s', which is not defined in actors." % trigger_ref))
    return issues


def rule_scripted_actor_autopilot(root):
    """Actor bị event điều khiển không được để behavior = autopilot."""
    issues = []
    controlled = set()
    for event in get_events(root):
        if not isinstance(event, dict):
            continue
        actor_ref = as_string(event.get("actor_ref"))
        if actor_ref is not None:
            controlled.add(actor_ref)
    if not controlled:
        return issues

    for index, actor in enumerate(get_actors(root)):
        if not isinstance(actor, dict):
            continue
        actor_id = as_string(actor.get("id"))
        if actor_id is None or actor_id not in controlled:
            continue
        if as_string(actor.get("behavior")) != "autopilot":
            continue
        issues.append(error(
            TIER_BUSINESS, "/actors/%d/behavior" % index,
            "Actor '%s' is controlled by an event and cannot use 'autopilot' behavior. "
            "Use 'scripted' or 'idle'." % actor_id))
    return issues


def rule_start_goal(root):
    """Điểm xuất phát và đích phải khác nhau."""
    route = root.get("route")
    if not isinstance(route, dict):
        return []
    if "start" not in route or "goal" not in route:
        return []
    if not are_equivalent(route["start"], route["goal"]):
        return []
    return [error(TIER_BUSINESS, "/route/goal",
                  "The goal matches the start position. Choose a different position.")]


def index_of_event(events, event_id):
    """Vị trí event có id (trùng id lấy event đầu); -1 nếu không có. Khớp ScenarioContentReader.IndexOfEvent."""
    for i, event in enumerate(events):
        if isinstance(event, dict) and as_string(event.get("id")) == event_id:
            return i
    return -1


def waits_for(events, index):
    event = events[index]
    trigger = event.get("trigger") if isinstance(event, dict) else None
    if isinstance(trigger, dict) and as_string(trigger.get("type")) == "after_event":
        return as_string(trigger.get("event_ref"))
    return None


def fire_time(events, index, parameters):
    """Giây trigger bắt đầu: simulation_time, hoặc after_event cộng dồn delay_s tới gốc theo giờ. None nếu không tính được."""
    total = 0.0
    seen = set()
    i = index
    while i >= 0 and i not in seen:
        seen.add(i)
        event = events[i]
        trigger = event.get("trigger") if isinstance(event, dict) else None
        if not isinstance(trigger, dict):
            return None
        kind = as_string(trigger.get("type"))
        if kind == "simulation_time":
            ok, time_s = try_get_number(trigger.get("time_s"), parameters)
            return total + time_s if ok else None
        if kind != "after_event":
            return None
        ok, delay = try_get_number(trigger.get("delay_s"), parameters)
        ref = as_string(trigger.get("event_ref"))
        if not ok or ref is None:
            return None
        total += delay
        i = index_of_event(events, ref)
    return None


def rule_event_chain(root):
    """after_event phải chờ event có thật và chuỗi chờ không vòng lại chính nó. Khớp EventChainRule."""
    issues = []
    events = get_events(root)
    for i in range(len(events)):
        ref = waits_for(events, i)
        if ref is None:
            continue
        path = "/events/%d/trigger/event_ref" % i
        if index_of_event(events, ref) < 0:
            issues.append(error(TIER_BUSINESS, path,
                                "The trigger waits for event '%s', which is not defined in events." % ref))
            continue
        seen = set()
        j = i
        while j not in seen:
            seen.add(j)
            nxt = waits_for(events, j)
            j = index_of_event(events, nxt) if nxt is not None else -1
            if j == i:
                issues.append(error(TIER_BUSINESS, path,
                                    "Event '%s' waits for itself through after_event triggers, so it never starts."
                                    % as_string(events[i].get("id"))))
                break
            if j < 0:
                break
    return issues


def rule_trigger_timeout(root):
    """Trigger theo thời gian mô phỏng (kể cả chuỗi after_event gốc theo giờ) phải xảy ra TRƯỚC execution.timeout_s."""
    issues = []
    parameters = get_parameters(root)
    execution = root.get("execution")
    if not isinstance(execution, dict):
        return issues
    ok, timeout = try_get_number(execution.get("timeout_s"), parameters)
    if not ok:
        return issues

    events = get_events(root)
    for index, event in enumerate(events):
        if not isinstance(event, dict):
            continue
        trigger = event.get("trigger")
        if not isinstance(trigger, dict):
            continue
        kind = as_string(trigger.get("type"))
        if kind not in ("simulation_time", "after_event"):
            continue
        time_s = fire_time(events, index, parameters)
        if time_s is None or time_s < timeout:
            continue
        if kind == "simulation_time":
            issues.append(error(
                TIER_BUSINESS, "/events/%d/trigger/time_s" % index,
                "The trigger fires at %ss, but the simulation ends at %ss. "
                "The event will never run." % (fmt_num(time_s), fmt_num(timeout))))
        else:
            issues.append(error(
                TIER_BUSINESS, "/events/%d/trigger/delay_s" % index,
                "The trigger fires at %ss, counting the events it waits for, but the simulation ends at %ss. "
                "The event will never run." % (fmt_num(time_s), fmt_num(timeout))))
    return issues


BUSINESS_RULES = (
    rule_parameter_range,
    rule_param_reference,
    rule_actor_reference,
    rule_scripted_actor_autopilot,
    rule_start_goal,
    rule_trigger_timeout,
    rule_event_chain,
)


# --------------------------------------------------------------------------
# Tầng 3 - đối chiếu map metadata xuất từ CARLA
# --------------------------------------------------------------------------
def load_map_metadata(metadata_dir):
    """Đọc mọi *.json trong thư mục, trả {map_name: {"spawn_point_count": n, "spawn_points": {index: (x, y, z)}}}.

    spawn_points đọc giống SpawnPointReader bên C#: bỏ phần tử không có index, thiếu toạ độ thì lấy 0.
    """
    maps = {}
    if not metadata_dir:
        return maps
    if not os.path.isdir(metadata_dir):
        raise SystemExit("Không tìm thấy thư mục metadata: %s" % metadata_dir)

    for name in sorted(os.listdir(metadata_dir)):
        if not name.endswith(".json"):
            continue
        with open(os.path.join(metadata_dir, name), "r", encoding="utf-8") as handle:
            data = json.load(handle)
        map_name = data.get("map_name")
        if not map_name:
            continue
        spawn_points = data.get("spawn_points")
        count = data.get("spawn_point_count")
        if count is None:
            count = len(spawn_points) if isinstance(spawn_points, list) else 0
        points = {}
        for node in spawn_points if isinstance(spawn_points, list) else []:
            if not isinstance(node, dict):
                continue
            ok, index = try_get_number(node.get("index"), {})
            if not ok:
                continue
            points[int(index)] = tuple(try_get_number(node.get(axis), {})[1] for axis in ("x", "y", "z"))
        maps[map_name] = {"map_name": map_name, "spawn_point_count": int(count), "spawn_points": points}
    return maps


# Ngưỡng của các luật vị trí/route - khớp hằng số trong Rules/Carla/*.cs
OFF_MAP_MARGIN_M = 50.0      # MapPositionRule.OffMapMarginM
FAR_FROM_ROAD_M = 50.0       # MapPositionRule.FarFromRoadM
MIN_SEPARATION_M = 1.0       # SpawnOverlapRule.MinSeparationM
TIGHT_TIMEOUT_RATIO = 0.5    # RouteFeasibilityRule.TightTimeoutRatio


def round1(value):
    """Làm tròn 1 chữ số thập phân giống ScenarioGeometry.Round1 = Math.Round(value*10)/10 (half-even)."""
    return round(value * 10) / 10


def resolve_location(node, parameters, points):
    """Khớp ScenarioGeometry.TryResolve: trả (x, y, z, spawn_index) hoặc None.

    spawn_point_index tra trong metadata; toạ độ lấy x/y/z, z mặc định 0.5 như bộ chuẩn hoá.
    """
    if not isinstance(node, dict):
        return None
    if "spawn_point_index" in node:
        ok, raw = try_get_number(node["spawn_point_index"], parameters)
        if not ok:
            return None
        index = int(raw)
        if index != raw or index not in points:
            return None
        x, y, z = points[index]
        return (x, y, z, index)
    ok_x, x = try_get_number(node.get("x"), parameters)
    ok_y, y = try_get_number(node.get("y"), parameters)
    if not (ok_x and ok_y):
        return None
    ok_z, z = try_get_number(node.get("z"), parameters)
    if not ok_z:
        z = 0.5
    return (x, y, z, None)


def scenario_locations(root):
    """Khớp ScenarioGeometry.Locations: [(path, chủ ngữ, node)] theo thứ tự start, waypoint, goal, actor, trigger."""
    result = []
    route = root.get("route")
    if isinstance(route, dict):
        result.append(("/route/start", "The route start", route.get("start")))
        waypoints = route.get("waypoints")
        if isinstance(waypoints, list):
            for index, waypoint in enumerate(waypoints):
                result.append(("/route/waypoints/%d" % index, "Waypoint %d" % (index + 1), waypoint))
        result.append(("/route/goal", "The route goal", route.get("goal")))
    for index, actor in enumerate(get_actors(root)):
        if isinstance(actor, dict):
            result.append(("/actors/%d/spawn" % index,
                           "Actor '%s'" % (as_string(actor.get("id")) or ""), actor.get("spawn")))
    for index, event in enumerate(get_events(root)):
        if not isinstance(event, dict):
            continue
        trigger = event.get("trigger")
        if isinstance(trigger, dict) and "location" in trigger:
            result.append(("/events/%d/trigger/location" % index,
                           "The trigger location of event '%s'" % (as_string(event.get("id")) or ""),
                           trigger["location"]))
        params = event.get("params")
        if isinstance(params, dict) and "destination" in params:
            result.append(("/events/%d/params/destination" % index,
                           "The destination of event '%s'" % (as_string(event.get("id")) or ""),
                           params["destination"]))
        sync = params.get("sync") if isinstance(params, dict) else None
        if isinstance(sync, dict) and "point" in sync:
            result.append(("/events/%d/params/sync/point" % index,
                           "The sync point of event '%s'" % (as_string(event.get("id")) or ""),
                           sync["point"]))
    return result


def dist2(a, b):
    """Khoảng cách ngang, khớp ScenarioGeometry.Distance2D."""
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    return math.sqrt(dx * dx + dy * dy)


def dist3(a, b):
    """Khoảng cách 3D, khớp ScenarioGeometry.Distance3D."""
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def validate_carla_tier(root, maps, metadata_available):
    """Bảy luật tầng 3, khớp Rules/Carla/*.cs."""
    issues = []
    map_name = None
    map_block = root.get("map")
    if isinstance(map_block, dict):
        map_name = as_string(map_block.get("name"))
    current = maps.get(map_name) if map_name else None

    # MapExistsRule
    if not metadata_available:
        issues.append(warning(
            TIER_CARLA, "/map/name",
            "CARLA map metadata has not been loaded. Tier 3 checks "
            "(map, spawn points, positions, route and NPC count) were skipped."))
    elif current is None and map_name is not None:
        issues.append(error(
            TIER_CARLA, "/map/name",
            "Map '%s' is not in the map metadata loaded from CARLA." % map_name))

    if current is None:
        return issues

    spawn_point_count = current["spawn_point_count"]
    parameters = get_parameters(root)

    # SpawnPointIndexRule
    for path, node in walk_objects(root):
        if "spawn_point_index" not in node:
            continue
        ok, index = try_get_number(node["spawn_point_index"], parameters)
        if not ok:
            continue
        if index >= spawn_point_count:
            issues.append(error(
                TIER_CARLA, "%s/spawn_point_index" % path,
                "Map '%s' has %d spawn points (indices 0..%d). Index %s is out of range."
                % (current["map_name"], spawn_point_count, spawn_point_count - 1, fmt_num(index))))

    # NpcCountRule
    traffic = root.get("traffic")
    npc = traffic.get("npc_vehicles") if isinstance(traffic, dict) else None
    if isinstance(npc, dict):
        ok, count = try_get_number(npc.get("count"), parameters)
        if ok:
            occupied = sum(
                1 for actor in get_actors(root)
                if isinstance(actor, dict)
                and isinstance(actor.get("spawn"), dict)
                and "spawn_point_index" in actor["spawn"])
            available = spawn_point_count - 1 - occupied  # -1 cho ego
            if count > available:
                issues.append(error(
                    TIER_CARLA, "/traffic/npc_vehicles/count",
                    "Map '%s' has %d free spawn points (%d total, excluding the ego vehicle and %d actors), "
                    "insufficient for %s NPC vehicles."
                    % (current["map_name"], available, spawn_point_count, occupied, fmt_num(count))))

    map_label = current["map_name"]
    points = current.get("spawn_points") or {}
    route = root.get("route") if isinstance(root.get("route"), dict) else None

    # MapPositionRule: toạ độ (x, y) phải nằm trong vùng spawn point phủ (+50 m), xa mọi spawn point > 50 m thì cảnh báo
    if points:
        spawns = list(points.values())
        min_x, max_x = min(p[0] for p in spawns), max(p[0] for p in spawns)
        min_y, max_y = min(p[1] for p in spawns), max(p[1] for p in spawns)
        for path, subject, location in scenario_locations(root):
            position = resolve_location(location, parameters, points)
            if position is None or position[3] is not None:
                continue
            x, y = position[0], position[1]
            outside = max(max(min_x - x, x - max_x), max(min_y - y, y - max_y))
            if outside > OFF_MAP_MARGIN_M:
                issues.append(error(
                    TIER_CARLA, path,
                    "%s is %s m outside map '%s', beyond the area covered by its spawn points."
                    % (subject, fmt_num(round1(outside)), map_label)))
                continue
            nearest = min(dist2(position, p) for p in spawns)
            if nearest > FAR_FROM_ROAD_M:
                issues.append(warning(
                    TIER_CARLA, path,
                    "%s is %s m from the nearest spawn point of map '%s' and may not be on a road. "
                    "Check the coordinates against the map." % (subject, fmt_num(round1(nearest)), map_label)))

    # SpawnOverlapRule: actor không được trùng spawn point / cách < 1 m với ego hay actor khai báo trước
    placed = []
    if route is not None:
        start = resolve_location(route.get("start"), parameters, points)
        if start is not None:
            placed.append(("the ego vehicle", start))
    for index, actor in enumerate(get_actors(root)):
        if not isinstance(actor, dict):
            continue
        position = resolve_location(actor.get("spawn"), parameters, points)
        if position is None:
            continue
        actor_id = as_string(actor.get("id")) or ""
        for other, at in placed:
            if position[3] is not None and at[3] == position[3]:
                reason = "both use spawn point #%d" % position[3]
            elif dist3(position, at) < MIN_SEPARATION_M:
                reason = "they are less than 1 m apart"
            else:
                continue
            issues.append(error(
                TIER_CARLA, "/actors/%d" % index,
                "Actor '%s' spawns on top of %s: %s. CARLA cannot spawn overlapping actors."
                % (actor_id, other, reason)))
            break
        placed.append(("actor '%s'" % actor_id, position))

    # RouteFeasibilityRule: goal trong goal_tolerance quanh start; đường thẳng ở speed limit lâu hơn timeout
    if route is not None:
        waypoints = route.get("waypoints") if isinstance(route.get("waypoints"), list) else []
        chain = []
        for node in [route.get("start")] + waypoints + [route.get("goal")]:
            position = resolve_location(node, parameters, points)
            if position is None:
                chain = None
                break
            chain.append(position)
        if chain is not None:
            ok, tolerance = try_get_number(route.get("goal_tolerance_m"), parameters)
            if not waypoints and ok:
                gap = dist3(chain[0], chain[-1])
                if gap <= tolerance:
                    issues.append(error(
                        TIER_CARLA, "/route/goal_tolerance_m",
                        "The goal is %s m from the start, within the %s m goal tolerance, "
                        "so route completion would be met as soon as the run starts."
                        % (fmt_num(round1(gap)), fmt_num(tolerance))))
            # target_speed_kmh của ego thấp hơn speed limit (hoặc không có limit) thì tính theo target speed
            ok_limit, limit = try_get_number(route.get("speed_limit_kmh"), parameters)
            has_limit = ok_limit and limit > 0
            ego = root.get("ego_vehicle")
            ok_target, target = (try_get_number(ego.get("target_speed_kmh"), parameters)
                                 if isinstance(ego, dict) else (False, 0))
            has_target = ok_target and target > 0
            by_target = has_target and (not has_limit or target < limit)
            speed = target if by_target else limit
            label, tail = (("ego target speed", "Only a run faster than the target speed could finish.")
                           if by_target else
                           ("speed limit", "Only a run that breaks the speed limit could finish."))
            execution = root.get("execution")
            ok_timeout, timeout = (try_get_number(execution.get("timeout_s"), parameters)
                                   if isinstance(execution, dict) else (False, 0))
            if (has_limit or has_target) and ok_timeout:
                length = 0.0
                for previous, current_point in zip(chain, chain[1:]):
                    length += dist2(previous, current_point)
                minimum = length / (speed / 3.6)
                time_text, distance_text = fmt_num(round1(minimum)), fmt_num(round1(length))
                if minimum > timeout:
                    issues.append(error(
                        TIER_CARLA, "/execution/timeout_s",
                        "Even in a straight line at the %s km/h %s, the ego needs at least %s s "
                        "to cover %s m between the route points, but the run times out after %s s. %s"
                        % (fmt_num(speed), label, time_text, distance_text, fmt_num(timeout), tail)))
                elif minimum > timeout * TIGHT_TIMEOUT_RATIO:
                    issues.append(warning(
                        TIER_CARLA, "/execution/timeout_s",
                        "In a straight line at the %s km/h %s, the ego already needs %s s of the "
                        "%s s timeout to cover %s m between the route points. Real routes are longer and "
                        "include stops, so the run may time out."
                        % (fmt_num(speed), label, time_text, fmt_num(timeout), distance_text)))

    # TriggerAtStartRule: trigger khoảng cách đã thoả ngay tại điểm xuất phát của ego (3D); trigger
    # time_to_collision: khoảng cách ÷ initial_speed_kmh của ego đã ≤ ttc_s (coi như tiến thẳng về actor)
    start = resolve_location(route.get("start"), parameters, points) if route is not None else None
    if start is not None:
        actor_spawns = {}
        for actor in get_actors(root):
            if isinstance(actor, dict):
                actor_id = as_string(actor.get("id"))
                if actor_id is not None and actor_id not in actor_spawns:
                    actor_spawns[actor_id] = actor.get("spawn")
        ego = root.get("ego_vehicle")
        ok_speed, ego_speed = try_get_number(ego.get("initial_speed_kmh"), parameters) if isinstance(ego, dict) else (False, 0)
        if not ok_speed:
            ego_speed = 0
        for index, event in enumerate(get_events(root)):
            if not isinstance(event, dict):
                continue
            trigger = event.get("trigger")
            if not isinstance(trigger, dict):
                continue
            if as_string(trigger.get("type")) == "time_to_collision":
                ok, threshold = try_get_number(trigger.get("ttc_s"), parameters)
                actor_ref = as_string(trigger.get("actor_ref"))
                if ego_speed <= 0 or not ok or actor_ref is None:
                    continue
                position = resolve_location(actor_spawns.get(actor_ref), parameters, points)
                if position is None:
                    continue
                distance = dist3(start, position)
                ttc = distance / (ego_speed / 3.6)
                if ttc <= threshold:
                    issues.append(warning(
                        TIER_CARLA, "/events/%d/trigger/ttc_s" % index,
                        "The ego starts %s m from actor '%s' at %s km/h, %s s away, within the %s s trigger time, "
                        "so event '%s' fires as soon as the run starts."
                        % (fmt_num(round1(distance)), actor_ref, fmt_num(ego_speed), fmt_num(round1(ttc)),
                           fmt_num(threshold), as_string(event.get("id")) or "")))
                continue
            ok, threshold = try_get_number(trigger.get("distance_m"), parameters)
            if not ok:
                continue
            kind = as_string(trigger.get("type"))
            actor_ref = as_string(trigger.get("actor_ref"))
            if kind == "ego_distance_to_actor" and actor_ref is not None:
                target, label = actor_spawns.get(actor_ref), "actor '%s'" % actor_ref
            elif kind == "ego_distance_to_location":
                target, label = trigger.get("location"), "the trigger location"
            else:
                continue
            position = resolve_location(target, parameters, points)
            if position is None:
                continue
            distance = dist3(start, position)
            if distance <= threshold:
                issues.append(warning(
                    TIER_CARLA, "/events/%d/trigger/distance_m" % index,
                    "The ego starts %s m from %s, within the %s m trigger distance, so event '%s' fires "
                    "as soon as the run starts."
                    % (fmt_num(round1(distance)), label, fmt_num(threshold), as_string(event.get("id")) or "")))

    return issues


# --------------------------------------------------------------------------
# Điều phối 3 tầng
# --------------------------------------------------------------------------
def validate(raw, schema, maps=None, metadata_available=False):
    """Chạy 3 tầng theo thứ tự, DỪNG ngay khi một tầng có lỗi."""
    maps = maps or {}
    warnings = []
    root = normalize(raw)

    for tier in TIER_ORDER:
        if tier == TIER_SCHEMA:
            issues = validate_schema_tier(raw, schema)
        elif tier == TIER_BUSINESS:
            issues = []
            for rule in BUSINESS_RULES:
                issues.extend(rule(root))
            # Luật cần schema để biết miền của trường được gắn $param
            issues.extend(rule_param_target_range(root, schema))
        else:
            issues = validate_carla_tier(root, maps, metadata_available)

        warnings.extend(i for i in issues if i.severity == "warning")
        errors = [i for i in issues if i.severity == "error"]
        if errors:
            return {
                "valid": False,
                "stopped_at": tier,
                "errors": [i.as_dict() for i in errors],
                "warnings": [i.as_dict() for i in warnings],
            }

    return {
        "valid": True,
        "stopped_at": None,
        "errors": [],
        "warnings": [i.as_dict() for i in warnings],
    }


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------
def load_document(path):
    """Đọc .json, hoặc .yaml/.yml nếu có pyyaml."""
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read()
    if path.endswith((".yaml", ".yml")):
        try:
            import yaml
        except ImportError:
            raise SystemExit("Đọc YAML cần pyyaml: pip install pyyaml")
        return yaml.safe_load(text)
    return json.loads(text)


def print_report(path, report):
    status = "HỢP LỆ" if report["valid"] else "KHÔNG HỢP LỆ (dừng ở tầng %s)" % report["stopped_at"]
    print("%s  %s" % (os.path.basename(path), status))
    for item in report["errors"]:
        print("  LỖI      [%s] %s  %s" % (item["tier"], item["path"], item["message"]))
    for item in report["warnings"]:
        print("  CẢNH BÁO [%s] %s  %s" % (item["tier"], item["path"], item["message"]))


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Validator 3 tầng tham chiếu cho scenario schema v1.0.")
    parser.add_argument("files", nargs="+", help="Các file scenario (.json hoặc .yaml)")
    parser.add_argument("--schema", default=DEFAULT_SCHEMA,
                        help="Đường dẫn scenario.schema.json (mặc định: bản trong Resources/)")
    parser.add_argument("--metadata-dir", default=None,
                        help="Thư mục chứa map metadata JSON xuất từ CARLA. "
                             "Không có thì tầng 3 chỉ cảnh báo.")
    parser.add_argument("--json", dest="as_json", action="store_true",
                        help="In report dạng JSON thay vì dạng đọc được")
    args = parser.parse_args(argv)

    with open(args.schema, "r", encoding="utf-8") as handle:
        schema = json.load(handle)

    maps = load_map_metadata(args.metadata_dir)
    metadata_available = bool(maps)

    reports = {}
    all_valid = True
    for path in args.files:
        report = validate(load_document(path), schema, maps, metadata_available)
        reports[path] = report
        all_valid = all_valid and report["valid"]
        if not args.as_json:
            print_report(path, report)

    if args.as_json:
        print(json.dumps(reports, ensure_ascii=False, indent=2))

    return 0 if all_valid else 1


if __name__ == "__main__":
    sys.exit(main())
