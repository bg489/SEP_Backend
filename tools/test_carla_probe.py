"""Native client ownership and stream teardown regressions; no simulator/GPU."""
import json
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import carla_probe as probe
import carla_recorder as recorder
import campaign_worker as worker


class ProbeTests(unittest.TestCase):
    def test_map_probe_owns_one_client_and_rejects_wrong_opt_world(self):
        client = Mock()
        client.get_server_version.return_value = "0.9.16"
        client.get_world.return_value.get_map.return_value.name = "Carla/Maps/Town01_Opt"
        factory = Mock(return_value=client)
        with patch.object(probe.time, "sleep"):
            result = probe.inspect_server("127.0.0.1", 2017, "Town01", 1, True, client_factory=factory)
        self.assertFalse(result["ok"])
        factory.assert_called_once_with("127.0.0.1", 2017, 1)
        client.get_world.assert_called_once()
        client.load_world.assert_not_called()

    def test_version_probe_never_subscribes_to_an_episode(self):
        client = Mock()
        client.get_server_version.return_value = "0.9.16"
        with patch.object(probe.time, "sleep"):
            result = probe.inspect_server("localhost", 2017, "", 1, client_factory=lambda *args: client)
        self.assertTrue(result["ok"])
        client.get_world.assert_not_called()

    def test_worker_waits_for_probe_exit_and_rejects_missing_or_failed_result(self):
        for code, output in [(1, 'ASX_CARLA_PROBE:{"ok":true}'), (0, "native process stopped")]:
            with self.subTest(code=code), patch.object(worker.subprocess, "run", return_value=SimpleNamespace(returncode=code, stdout=output)) as run:
                result = worker.probe_carla("localhost", 2017, 2, "Town01", route=True)
                self.assertFalse(result["ok"])
                self.assertIn("carla_probe.py", " ".join(run.call_args.args[0]))
                self.assertEqual(run.call_args.kwargs["timeout"], 12)

    def test_cleanup_stops_streams_before_destruction_then_restores_world(self):
        steps = []
        sensor = SimpleNamespace(stop=lambda: steps.append("stop"))
        world = SimpleNamespace(tick=lambda: steps.append("tick"), apply_settings=lambda value: steps.append("restore"))
        client = SimpleNamespace(set_timeout=lambda value: None, apply_batch_sync=lambda commands, tick: steps.append("destroy") or [])
        tm = SimpleNamespace(set_synchronous_mode=lambda value: steps.append("tm-off"))
        carla = SimpleNamespace(command=SimpleNamespace(DestroyActor=lambda actor: actor))
        errors = recorder.cleanup_recording(client, world, tm, carla, [sensor], [], [object()], object(), [])
        self.assertEqual(errors, [])
        self.assertEqual(steps, ["stop", "tick", "destroy", "destroy", "tm-off", "restore"])

    def test_cleanup_failure_is_exposed_without_skipping_world_restore(self):
        client = Mock()
        client.apply_batch_sync.side_effect = RuntimeError("simulator disconnected")
        world, tm = Mock(), Mock()
        carla = SimpleNamespace(command=SimpleNamespace(DestroyActor=lambda actor: actor))
        errors = recorder.cleanup_recording(client, world, tm, carla, [Mock()], [], [], object(), [])
        self.assertIn("simulator disconnected", " ".join(errors))
        world.apply_settings.assert_called_once()


if __name__ == "__main__":
    unittest.main()
