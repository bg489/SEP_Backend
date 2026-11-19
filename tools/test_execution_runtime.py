"""Recorder action/trigger and worker protocol tests. No CARLA, API or GPU is contacted."""
import json
import math
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import carla_recorder as recorder
import campaign_worker as worker


class Vector:
    def __init__(self, x=0, y=0, z=0):
        self.x, self.y, self.z = x, y, z
    def __add__(self, other):
        return Vector(self.x + other.x, self.y + other.y, self.z + other.z)
    def __sub__(self, other):
        return Vector(self.x - other.x, self.y - other.y, self.z - other.z)
    def __mul__(self, factor):
        return Vector(self.x * factor, self.y * factor, self.z * factor)
    def length(self):
        return math.sqrt(self.x**2 + self.y**2 + self.z**2)


class RuntimeTests(unittest.TestCase):
    def test_controller_lookahead_does_not_mutate_the_declared_route(self):
        def waypoint(x):
            return SimpleNamespace(transform=SimpleNamespace(rotation=SimpleNamespace(yaw=0)),
                                   next=lambda distance: [waypoint(x + distance)], x=x)
        route = [(waypoint(0), "lane_follow"), (waypoint(30), "lane_follow")]
        expanded = recorder.controller_lookahead(route)
        self.assertEqual(len(route), 2)
        self.assertEqual(expanded[1][0].x, 30)
        self.assertEqual(expanded[-1][0].x, 42)
        self.assertEqual(len(expanded), 8)

    def test_windows_progress_reader_does_not_abort_the_run(self):
        with tempfile.TemporaryDirectory() as folder:
            path = str(Path(folder) / "progress.json")
            with patch.object(recorder.os, "replace", side_effect=PermissionError("reader holds file")):
                self.assertFalse(recorder.write_progress_snapshot(path, {"frame": 1}))
            self.assertTrue(recorder.write_progress_snapshot(path, {"frame": 2}))
            self.assertEqual(json.loads(Path(path).read_text())["frame"], 2)

    def setUp(self):
        self.carla = SimpleNamespace(Vector3D=Vector, Location=Vector,
            Rotation=lambda **kwargs: SimpleNamespace(**kwargs),
            Transform=lambda location, rotation: SimpleNamespace(location=location, rotation=rotation),
            WalkerControl=lambda **kwargs: kwargs,
            TrafficLightState=SimpleNamespace(Red="red-state", Yellow="yellow-state", Green="green-state"))
        self.ego = SimpleNamespace(location=Vector(), get_forward_vector=lambda: Vector(1, 0, 0),
                                   get_right_vector=lambda: Vector(0, 1, 0))
        self.actor, self.tm = Mock(), Mock()
        self.actor.get_location.return_value = Vector(0, -3, 0)

    def test_after_event_waits_for_reference_and_delay(self):
        trigger = {"type": "after_event", "event_ref": "crossing", "delay_s": 2}
        self.assertFalse(recorder.trigger_fired(trigger, 10, (0, 0, 0), {}, None, {}))
        self.assertFalse(recorder.trigger_fired(trigger, 3.9, (0, 0, 0), {}, None, {"crossing": 2}))
        self.assertTrue(recorder.trigger_fired(trigger, 4, (0, 0, 0), {}, None, {"crossing": 2}))

    def test_ttc_only_fires_for_a_closing_actor(self):
        trigger = {"type": "time_to_collision", "actor_ref": "lead", "ttc_s": 2}
        positions = {"lead": (10, 0, 0)}
        self.assertTrue(recorder.trigger_fired(trigger, 0, (0, 0, 0), positions, None,
            ego_velocity=(5, 0, 0), velocities={"lead": (0, 0, 0)}))
        self.assertFalse(recorder.trigger_fired(trigger, 0, (0, 0, 0), positions, None,
            ego_velocity=(5, 0, 0), velocities={"lead": (10, 0, 0)}))
        self.assertFalse(recorder.trigger_fired(trigger, 0, (0, 0, 0), positions, None,
            ego_velocity=(0, 0, 0), velocities={"lead": (0, 0, 0)}))

    def test_ttc_requires_observed_velocity_and_never_guesses_zero(self):
        with self.assertRaisesRegex(ValueError, "measured"):
            recorder.trigger_fired({"type": "time_to_collision", "actor_ref": "lead", "ttc_s": 2},
                0, (0, 0, 0), {"lead": (10, 0, 0)}, None)

    def test_unknown_event_or_trigger_is_rejected_before_simulator_start(self):
        for event_type, trigger_type in [("unimplemented", "simulation_time"), ("pedestrian_crossing", "unknown")]:
            with self.subTest(event_type=event_type, trigger_type=trigger_type), self.assertRaises(ValueError):
                recorder.validate_execution_contract({"events": [{"id": "event", "type": event_type, "trigger": {"type": trigger_type}}]})

    def test_supported_contract_includes_all_five_actions_and_both_ego_agents(self):
        self.assertEqual(recorder.SUPPORTED_EVENTS, {"pedestrian_crossing", "lead_vehicle_brake", "vehicle_crossing", "vehicle_cut_in", "traffic_light_override"})
        for sut in ("behavior_agent", "autopilot"):
            recorder.validate_execution_contract({"events": []}, sut)
        with self.assertRaisesRegex(ValueError, "controller"):
            recorder.validate_execution_contract({"events": []}, "npc-autopilot")

    def test_unimplemented_synchronized_arrival_is_explicitly_rejected(self):
        event = {"id": "synchronized", "type": "pedestrian_crossing", "trigger": {"type": "simulation_time"},
                 "params": {"sync": {"point": {"x": 1, "y": 2}}}}
        with self.assertRaisesRegex(ValueError, "synchronized arrival"):
            recorder.validate_execution_contract({"events": [event]})

    def test_pedestrian_crossing_obeys_direction_and_speed(self):
        recorder._fire(self.carla, {"type": "pedestrian_crossing", "params": {"speed_ms": 1.5, "direction": "right_to_left"}},
                       self.actor, self.ego, 8000, [])
        control = self.actor.apply_control.call_args.args[0]
        self.assertEqual(control["speed"], 1.5)
        self.assertEqual(control["direction"].y, -1)

    def test_brake_action_removes_autopilot_and_uses_requested_deceleration(self):
        braking = []
        recorder._fire(self.carla, {"type": "lead_vehicle_brake", "params": {"deceleration_ms2": 3, "target_speed_kmh": 18}},
                       self.actor, self.ego, 8000, braking)
        self.actor.set_autopilot.assert_called_once_with(False, 8000)
        self.assertEqual(braking, [(self.actor, 3, 5)])

    def test_vehicle_cut_in_uses_gap_duration_and_rejects_wrong_start_side(self):
        event = {"id": "cut", "type": "vehicle_cut_in", "params": {"side": "left", "cut_in_gap_m": 7, "lane_change_duration_s": 2}}
        movements = []
        recorder._fire(self.carla, event, self.actor, self.ego, 8000, [], tm=self.tm, movements=movements, t=5, ego_speed=10)
        self.assertEqual(movements[0]["end"].x, 27)
        self.assertEqual(movements[0]["until"], 7)
        self.actor.get_location.return_value = Vector(0, 3, 0)
        with self.assertRaisesRegex(RuntimeError, "configured left"):
            recorder._fire(self.carla, event, self.actor, self.ego, 8000, [], tm=self.tm, movements=[])

    def test_vehicle_crossing_applies_route_speed_and_red_light_behavior(self):
        event = {"type": "vehicle_crossing", "params": {"destination": {"x": 50, "y": 4}, "speed_kmh": 30, "ignore_traffic_lights": True}}
        recorder._fire(self.carla, event, self.actor, self.ego, 8000, [], tm=self.tm)
        self.actor.set_autopilot.assert_called_once_with(True, 8000)
        self.tm.set_desired_speed.assert_called_once_with(self.actor, 30)
        self.tm.ignore_lights_percentage.assert_called_once_with(self.actor, 100)
        self.assertEqual(self.tm.set_path.call_args.args[1][0].x, 50)

    def test_light_override_is_timed_and_preserves_the_original_state(self):
        light = Mock()
        light.get_location.return_value = Vector(20, 0, 0)
        light.get_group_traffic_lights.return_value = [light]
        light.get_state.return_value = "green-state"
        light.is_frozen.return_value = False
        world = Mock()
        world.get_actors.return_value.filter.return_value = [light]
        overrides = []
        recorder._fire(self.carla, {"id": "light", "type": "traffic_light_override", "params": {"state": "red", "duration_s": 5}},
                       None, self.ego, 8000, [], world=world, light_overrides=overrides, t=2)
        light.set_state.assert_called_once_with("red-state")
        self.assertEqual(overrides, [(light, "green-state", False, 7)])

    def test_light_override_fails_when_there_is_no_light_ahead(self):
        world = Mock()
        world.get_actors.return_value.filter.return_value = []
        with self.assertRaisesRegex(RuntimeError, "no next traffic light"):
            recorder._fire(self.carla, {"id": "light", "type": "traffic_light_override", "params": {"state": "red", "duration_s": 5}},
                           None, self.ego, 8000, [], world=world, light_overrides=[])

    def test_blocked_spawn_does_not_move_the_scenario_to_an_unrelated_road(self):
        world = Mock()
        world.try_spawn_actor.return_value = None
        with self.assertRaisesRegex(RuntimeError, "configured position"):
            recorder._spawn_vehicle(world, self.carla, Mock(), {"x": 1, "y": 2, "z": 1}, "ego")
        world.get_map.assert_not_called()
        world.try_spawn_actor.assert_called_once()

    def test_worker_claim_sends_registered_worker_and_campaign_ids(self):
        with patch.object(worker, "request", return_value=(204, None)) as request:
            self.assertIsNone(worker.claim("http://fixture", "campaign-id", "worker-id"))
        self.assertIn("campaignId=campaign-id", request.call_args.args[2])
        self.assertIn("workerId=worker-id", request.call_args.args[2])

    def test_worker_uses_supabase_bearer_token_without_dev_role_when_provided(self):
        response = Mock()
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        response.status, response.read.return_value = 204, b""
        with patch.dict(os.environ, {"ASX_ACCESS_TOKEN": "fixture-token"}), patch.object(worker.urllib.request, "urlopen", return_value=response) as urlopen:
            worker.request("http://fixture", "POST", "/api/v1/runs/claim")
        headers = dict((key.lower(), value) for key, value in urlopen.call_args.args[0].header_items())
        self.assertEqual(headers["authorization"], "Bearer fixture-token")
        self.assertNotIn("x-asx-dev-role", headers)


if __name__ == "__main__":
    unittest.main()
