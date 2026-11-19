#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Kiểm phần thuần của carla_recorder.py (không cần CARLA): python tools/test_carla_recorder.py"""
import json
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import carla_recorder as r

LOCATIONS = {"/route/start": {"x": 1.0, "y": 2.0, "z": 0.5, "yaw": 90.0}}


def test_resolve_location():
    # spawn index -> toạ độ đã giải trong gói; toạ độ -> dùng thẳng, z mặc định 0.5
    assert r.resolve_location({"spawn_point_index": 12}, "/route/start", LOCATIONS) == LOCATIONS["/route/start"]
    assert r.resolve_location({"x": 3, "y": 4}, "/actors/0/spawn", {}) == {"x": 3, "y": 4, "z": 0.5, "yaw": 0.0}
    try:
        r.resolve_location({"spawn_point_index": 9}, "/route/goal", LOCATIONS)
        assert False, "missing location must raise"
    except ValueError as e:
        assert "/route/goal" in str(e)


def test_triggers():
    ego = (0.0, 0.0, 0.0)
    actors = {"ped_01": (10.0, 0.0, 0.0)}
    near = {"type": "ego_distance_to_actor", "actor_ref": "ped_01", "distance_m": 10}
    far = {"type": "ego_distance_to_actor", "actor_ref": "ped_01", "distance_m": 9.9}
    assert r.trigger_fired(near, 0, ego, actors, None)
    assert not r.trigger_fired(far, 0, ego, actors, None)

    loc = {"type": "ego_distance_to_location", "distance_m": 5}
    assert r.trigger_fired(loc, 0, ego, actors, {"x": 3, "y": 4, "z": 0})
    assert not r.trigger_fired(loc, 0, ego, actors, {"x": 3, "y": 4.1, "z": 0})

    timed = {"type": "simulation_time", "time_s": 2}
    assert not r.trigger_fired(timed, 1.95, ego, actors, None)
    assert r.trigger_fired(timed, 2.0, ego, actors, None)


def test_impact_side():
    fwd, right = (1.0, 0.0), (0.0, 1.0)
    # impulse đẩy ego ra xa điểm va chạm: bị đâm từ sau -> impulse hướng về trước
    assert r.impact_side(fwd, right, (5.0, 0.0)) == "rear"
    assert r.impact_side(fwd, right, (-5.0, 1.0)) == "front"
    assert r.impact_side(fwd, right, (0.0, -3.0)) == "right"
    assert r.impact_side(fwd, right, (1.0, 3.0)) == "left"
    assert r.impact_side(fwd, right, (0.0, 0.0)) == "front"


def test_chase_camera_sits_behind_and_above():
    # ego hướng +x (yaw 0): camera lùi về -x; hướng +y (yaw 90): camera lùi về -y
    x, y, z, pitch, yaw = r.chase_camera(10.0, 0.0, 0.5, 0.0)
    assert (round(x, 6), round(y, 6), z, pitch, yaw) == (2.0, 0.0, 4.0, -15.0, 0.0)
    x, y, _, _, yaw = r.chase_camera(0.0, 10.0, 0.5, 90.0)
    assert (round(x, 6), round(y, 6), yaw) == (0.0, 2.0, 90.0)


def test_writer_always_ends():
    path = os.path.join(tempfile.mkdtemp(), "run.jsonl")
    try:
        with r.TelemetryFile(path, {"run_id": "x"}) as f:
            f.tick({"t": 0.0, "post_stop": False})
            raise RuntimeError("CARLA server stopped responding")
    except RuntimeError:
        pass
    lines = [json.loads(l) for l in open(path, encoding="utf-8")]
    assert lines[0] == {"kind": "header", "format": "asx.telemetry/1", "run_id": "x"}
    assert lines[1]["kind"] == "tick"
    assert lines[-1] == {"kind": "end", "t": 0.0, "outcome": "INFRA_FAILED", "reason": "CARLA server stopped responding"}

    with r.TelemetryFile(path, {}) as f:
        f.tick({"t": 3.5, "post_stop": False})
        f.finish("COMPLETED")
    assert json.loads(open(path, encoding="utf-8").read().splitlines()[-1]) == {"kind": "end", "t": 3.5, "outcome": "COMPLETED"}


def test_infra_failed_telemetry():
    lines = [json.loads(l) for l in r.infra_failed_telemetry("abc", "no CARLA").splitlines()]
    assert lines[0] == {"kind": "header", "format": "asx.telemetry/1", "run_id": "abc"}
    assert lines[1] == {"kind": "end", "t": 0.0, "outcome": "INFRA_FAILED", "reason": "no CARLA"}


def test_crossed_stop_line_only_when_the_bumper_passes_through():
    # Vạch dừng tại x=10, ngang làn từ y=-2 đến y=2, hướng xe +x.
    line = r.stop_line_segment(10.0, 0.0, 0.0, 2.0)
    assert r.crossed_stop_line((9.2, 0.0), (10.4, 0.0), [line])
    # Phanh và dừng trước vạch.
    assert not r.crossed_stop_line((8.0, 0.0), (9.6, 0.0), [line])
    # Chạy song song, không cắt đoạn vạch.
    assert not r.crossed_stop_line((9.2, 6.0), (10.4, 6.0), [line])
    assert not r.crossed_stop_line(None, (10.4, 0.0), [line])
    # Đầu xe: ego tại (0, 0) hướng +x, nửa chiều dài 2.4 m -> x = 2.4
    assert r.front_bumper(0.0, 0.0, 0.0, 2.4) == (2.4, 0.0)


def test_stopping_at_a_red_light_is_not_a_run():
    # Cắt vạch tại x=0, phanh và dừng sau 1 m.
    pending = (0.0, 0.0, False)
    pending, ran = r.judge_red_light(pending, True, 5.0, 1.0, 0.0)
    assert ran is False
    assert pending == (0.0, 0.0, False)
    pending, ran = r.judge_red_light(pending, True, 0.0, 1.2, 0.0)
    assert ran is False
    assert pending[2] is True
    # Đèn xanh rồi đi tiếp: không fail.
    pending, ran = r.judge_red_light(pending, False, 6.0, 12.0, 0.0)
    assert (pending, ran) == (None, False)


def test_driving_through_a_red_light_is_a_run():
    pending = (0.0, 0.0, False)
    pending, ran = r.judge_red_light(pending, True, 6.0, 8.0, 0.0)
    assert (pending, ran) == (None, True)
    # Đã dừng rồi lại phóng tiếp khi đèn vẫn đỏ.
    pending, ran = r.judge_red_light((0.0, 0.0, True), True, 6.0, 8.0, 0.0)
    assert ran is True


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
            print("ok", name)
