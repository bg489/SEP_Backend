#!/usr/bin/env python3
"""Offline worker regressions. No API, CARLA process or database is contacted.

Run from the repository: python -m unittest discover -s tools -p test_campaign_worker.py -v
"""
from __future__ import annotations
import campaign_worker as worker
import carla_maps

from contextlib import ExitStack, redirect_stdout
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


EXPECTED_MAPS = (
    "Town01", "Town01_Opt", "Town02", "Town02_Opt", "Town03", "Town03_Opt",
    "Town04", "Town04_Opt", "Town05", "Town05_Opt", "Town10HD", "Town10HD_Opt",
)


class MapNamesTests(unittest.TestCase):
    def test_catalogue_is_exactly_the_twelve_supported_maps(self):
        self.assertEqual(set(carla_maps.SUPPORTED_MAPS), set(EXPECTED_MAPS))
        self.assertEqual(len(carla_maps.SUPPORTED_MAPS), 12)

    def test_names_and_carla_asset_paths_resolve_to_the_same_exact_map(self):
        for name in EXPECTED_MAPS:
            with self.subTest(name=name):
                self.assertEqual(carla_maps.normalize_map_name(name), name)
                self.assertEqual(carla_maps.normalize_map_name(
                    f"/Game/Carla/Maps/{name}.{name}"), name)
                self.assertEqual(carla_maps.normalize_map_name(
                    f"Carla/Maps/{name}"), name)
                self.assertEqual(carla_maps.normalize_map_name(
                    f"Carla\\Maps\\{name}"), name)
        self.assertNotEqual(carla_maps.normalize_map_name(
            "Town01"), carla_maps.normalize_map_name("Town01_Opt"))
        self.assertNotEqual(carla_maps.normalize_map_name(
            "Town10HD"), carla_maps.normalize_map_name("Town10HD_Opt"))

    def test_unknown_names_and_path_traversal_are_rejected(self):
        for name in ("Town06", "Town07_Opt", "OpenDriveMap", "Town01_Opt_extra", "../Town01", "Town01/../../Town02", "", None):
            with self.subTest(name=name):
                with self.assertRaises((ValueError, TypeError)):
                    carla_maps.normalize_map_name(name)

    def test_discovery_uses_actual_map_assets_and_excludes_opendrive(self):
        with tempfile.TemporaryDirectory() as directory:
            maps = Path(directory) / "CarlaUE4" / "Content" / "Carla" / "Maps"
            maps.mkdir(parents=True)
            for name in EXPECTED_MAPS:
                (maps / f"{name}.umap").touch()
            (maps / "OpenDriveMap.umap").touch()
            (maps / "Town06.bin").touch()
            self.assertEqual(
                set(carla_maps.discover_installed_maps(directory)), set(EXPECTED_MAPS))
            (maps / "Town01_Opt.umap").unlink()
            self.assertNotIn(
                "Town01_Opt", carla_maps.discover_installed_maps(directory))


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.maps = self.root / "CarlaUE4" / "Content" / "Carla" / "Maps"
        self.maps.mkdir(parents=True)
        for name in EXPECTED_MAPS:
            (self.maps / f"{name}.umap").touch()
        (self.root / "CarlaUE4.exe").touch()
        self.args = SimpleNamespace(
            api="http://fixture.invalid", carla_root=str(self.root), boot_map="", fixed_town="",
            fixture=None, simulate=False, windowed=True, host="localhost", port=2000,
            tm_port=8000, carla_boot_timeout=5, profile="normal", max_infra_retries=0,
            no_video=True, lite=False, max_sim_s=None, out=str(self.root / "recordings"),
        )
        self.environment = patch.dict(
            os.environ, {"CARLA_ROOT": str(self.root)}, clear=False)
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def test_wait_map_does_not_accept_opt_as_the_plain_map(self):
        fake_carla = Mock()
        fake_carla.Client.return_value.get_world.return_value.get_map.return_value.name = "Carla/Maps/Town01_Opt"
        clock = [0.0]
        with patch.dict(sys.modules, {"carla": fake_carla}), patch.object(worker.time, "time", side_effect=lambda: clock[0]), \
                patch.object(worker.time, "sleep", side_effect=lambda seconds: clock.__setitem__(0, clock[0] + seconds)):
            self.assertFalse(worker._wait_map(
                "localhost", 2000, "Town01", timeout=1))
            self.assertTrue(worker._wait_map(
                "localhost", 2000, "Town01_Opt", timeout=1))

    def test_same_map_reuses_the_running_server(self):
        with patch.object(worker, "carla_ping", return_value=True), patch.object(worker, "_wait_map", return_value=True), \
                patch.object(worker, "_carla_is_offscreen", return_value=False), patch.object(worker, "kill_carla") as kill, \
                patch.object(worker, "start_carla") as start:
            worker.ensure_carla(self.args, "Town03")
            kill.assert_not_called()
            start.assert_not_called()

    def test_next_scenario_can_restart_on_a_different_exact_map(self):
        for name in EXPECTED_MAPS:
            with self.subTest(name=name), patch.object(worker, "carla_ping", return_value=True), \
                    patch.object(worker, "_wait_map", side_effect=[False, True]) as wait, \
                    patch.object(worker, "_carla_is_offscreen", return_value=False), \
                    patch.object(worker, "kill_carla") as kill, patch.object(worker, "start_carla") as start, \
                    patch.object(worker.time, "sleep"):
                worker.ensure_carla(self.args, name)
                kill.assert_called_once_with(str(self.root), self.args.port)
                start.assert_called_once_with(self.args, boot_map=name)
                self.assertTrue(
                    all(call.args[2] == name for call in wait.call_args_list))

    def test_unknown_or_uninstalled_map_never_stops_the_current_server(self):
        (self.maps / "Town04.umap").unlink()
        for name in ("Town99", "Town04"):
            with self.subTest(name=name), patch.object(worker, "kill_carla") as kill, patch.object(worker, "start_carla") as start, \
                    patch.object(worker, "carla_ping", return_value=True), patch.object(worker, "_wait_map", return_value=True), \
                    patch.object(worker, "_carla_is_offscreen", return_value=False):
                with self.assertRaises((ValueError, RuntimeError, FileNotFoundError)):
                    worker.ensure_carla(self.args, name)
                kill.assert_not_called()
                start.assert_not_called()

    def test_start_command_keeps_low_quality_and_exact_opt_asset(self):
        self.args.port = 2010
        for name in EXPECTED_MAPS:
            with self.subTest(name=name), patch.object(worker.subprocess, "Popen") as popen:
                worker.start_carla(self.args, name)
            command = popen.call_args.args[0]
            self.assertIn("-quality-level=Low", command)
            self.assertIn("-carla-rpc-port=2010", command)
            self.assertIn(f"/Game/Carla/Maps/{name}", command)
            self.assertEqual(sum(item.startswith("/Game/Carla/Maps/")
                             for item in command), 1)
            self.assertNotIn(
                ".", next(item for item in command if item.startswith("/Game/Carla/Maps/")))
            engine = self.root / "CarlaUE4" / "Saved" / \
                "Config" / "WindowsNoEditor" / "Engine.ini"
            self.assertIn(
                f"GameDefaultMap=/Game/Carla/Maps/{name}.{name}", engine.read_text(encoding="utf-8"))

    def test_list_maps_is_an_offline_preflight_without_claiming_or_starting(self):
        output = io.StringIO()
        with patch.object(sys, "argv", ["campaign_worker.py", "--carla-root", str(self.root), "--list-maps"]), \
                patch.object(worker, "claim") as claim, patch.object(worker, "start_carla") as start, \
                patch.object(worker, "request") as request, redirect_stdout(output):
            worker.main()
        claim.assert_not_called()
        start.assert_not_called()
        request.assert_not_called()
        for name in EXPECTED_MAPS:
            self.assertIn(name, output.getvalue())

    def test_invalid_legacy_fixed_town_is_rejected_before_claiming(self):
        with patch.object(sys, "argv", ["campaign_worker.py", "--carla-root", str(self.root), "--fixed-town", "Town99", "--once"]), \
                patch.object(worker, "claim") as claim, patch.object(worker, "start_carla") as start:
            with self.assertRaises(ValueError):
                worker.main()
        claim.assert_not_called()
        start.assert_not_called()

    def test_stale_town10_environment_no_longer_locks_the_worker(self):
        with patch.dict(os.environ, {"ASX_FIXED_MAP": "Town10HD_Opt", "ASX_API": "http://fixture.invalid"}), \
                patch.object(sys, "argv", ["campaign_worker.py", "--carla-root", str(self.root), "--once"]), \
                patch.object(worker, "claim", return_value=None) as claim, patch.object(worker, "start_carla") as start, \
                patch.object(worker, "register_worker", return_value="worker-fixture"), patch.object(worker, "heartbeat"):
            worker.main()
            self.assertNotIn("ASX_FIXED_MAP", os.environ)
        claim.assert_called_once_with("http://fixture.invalid", None, "worker-fixture")
        start.assert_not_called()

    def _engine_config(self):
        path = self.root / "CarlaUE4" / "Config" / "DefaultEngine.ini"
        path.parent.mkdir(parents=True, exist_ok=True)
        original = b"\xef\xbb\xbf" + (
            "; Existing packaged configuration\r\n"
            "[/Script/EngineSettings.GameMapsSettings]\r\n"
            "EditorStartupMap=/Game/Carla/Maps/Town10HD_Opt.Town10HD_Opt\r\n"
            "GameDefaultMap=/Game/Carla/Maps/Town10HD_Opt.Town10HD_Opt\r\n"
            "ServerDefaultMap=/Game/Carla/Maps/Town10HD_Opt.Town10HD_Opt\r\n"
            "TransitionMap=/Game/Carla/Maps/Town10HD_Opt.Town10HD_Opt\r\n"
            "GameInstanceClass=/Script/Carla.CarlaGameInstance\r\n"
            "\r\n[OtherSection]\r\n"
            "GameDefaultMap=unrelated-value-must-stay\r\n"
            "KeepMe=42\r\n"
        ).encode("utf-8")
        path.write_bytes(original)
        return path, original

    def test_packaged_map_override_preserves_other_keys_bom_and_crlf(self):
        path, original = self._engine_config()
        worker._set_packaged_boot_map(self.args, str(self.root), "Town02_Opt")
        modified = path.read_bytes()
        self.assertTrue(modified.startswith(b"\xef\xbb\xbf"))
        self.assertNotIn(b"\n", modified.replace(b"\r\n", b""))
        self.assertIn(
            b"GameInstanceClass=/Script/Carla.CarlaGameInstance\r\n", modified)
        self.assertIn(
            b"[OtherSection]\r\nGameDefaultMap=unrelated-value-must-stay\r\nKeepMe=42\r\n", modified)
        for key in ("EditorStartupMap", "GameDefaultMap", "ServerDefaultMap", "TransitionMap"):
            self.assertIn(
                f"{key}=/Game/Carla/Maps/Town02_Opt.Town02_Opt\r\n".encode(), modified)
        self.assertEqual(path.with_name(
            "DefaultEngine.ini.asx-worker-backup").read_bytes(), original)
        worker._restore_packaged_boot_map(self.args)
        self.assertEqual(path.read_bytes(), original)

    def test_successful_boot_restores_the_exact_original_packaged_bytes(self):
        path, original = self._engine_config()
        with patch.object(worker, "carla_ping", side_effect=[False, True]), \
                patch.object(worker, "kill_carla"), patch.object(worker, "_wait_map", return_value=True), \
                patch.object(worker.time, "sleep"), patch.object(worker.subprocess, "Popen"):
            worker.ensure_carla(self.args, "Town02")
        self.assertEqual(path.read_bytes(), original)
        self.assertIsNone(getattr(self.args, "_boot_config_restore", None))

    def test_failed_process_start_restores_the_exact_original_packaged_bytes(self):
        path, original = self._engine_config()
        with patch.object(worker, "carla_ping", return_value=False), patch.object(worker, "kill_carla"), \
                patch.object(worker.time, "sleep"), patch.object(worker.subprocess, "Popen", side_effect=OSError("fixture process launch failure")):
            with self.assertRaisesRegex(OSError, "launch failure"):
                worker.ensure_carla(self.args, "Town04_Opt")
        self.assertEqual(path.read_bytes(), original)
        self.assertIsNone(getattr(self.args, "_boot_config_restore", None))

    def test_missing_packaged_config_is_not_created_or_backed_up(self):
        path = self.root / "CarlaUE4" / "Config" / "DefaultEngine.ini"
        worker._set_packaged_boot_map(self.args, str(self.root), "Town02")
        worker._restore_packaged_boot_map(self.args)
        self.assertFalse(path.exists())
        self.assertFalse(path.with_name(
            "DefaultEngine.ini.asx-worker-backup").exists())

    def test_existing_recovery_copy_is_not_overwritten(self):
        path, original = self._engine_config()
        recovery = path.with_name("DefaultEngine.ini.asx-worker-backup")
        recovery.write_bytes(b"earlier recovery bytes")
        worker._set_packaged_boot_map(self.args, str(self.root), "Town02")
        worker._restore_packaged_boot_map(self.args)
        self.assertEqual(recovery.read_bytes(), b"earlier recovery bytes")
        self.assertEqual(path.read_bytes(), original)

    def _claimed(self, suffix="one"):
        return {"runId": f"run-{suffix}", "scenarioVersionId": f"version-{suffix}", "sutBehavior": "cautious", "seed": 73,
                "infraRetryCount": 0, "maxInfraRetries": 0}

    def test_package_fetch_failure_reports_infra_instead_of_leaving_the_claim(self):
        with patch.object(worker, "fetch_package", side_effect=RuntimeError("fixture package unavailable")), \
                patch.object(worker, "put_telemetry", return_value={"verdict": "ERROR", "outcome": "INFRA_FAILED"}) as upload, \
                patch.object(worker, "ensure_carla") as ensure, patch.object(worker, "mark_running"), \
                patch.object(worker, "requeue_infra") as requeue:
            result = worker.run_one(
                "http://fixture.invalid", self._claimed(), self.args)
        self.assertEqual(result["verdict"], "ERROR")
        records = [json.loads(line)
                   for line in upload.call_args.args[2].splitlines()]
        self.assertEqual(records[-1]["outcome"], "INFRA_FAILED")
        self.assertIn("package unavailable", records[-1]["reason"])
        ensure.assert_not_called()
        requeue.assert_not_called()

    def test_map_boot_failure_reports_infra_without_calling_the_recorder(self):
        with patch.object(worker, "fetch_package", return_value={"runnable": True, "map": {"name": "Town05"}}), \
                patch.object(worker, "ensure_carla", side_effect=RuntimeError("fixture CARLA boot timeout")), \
                patch.object(worker, "put_telemetry", return_value={"verdict": "ERROR", "outcome": "INFRA_FAILED"}) as upload, \
                patch.object(worker, "mark_running"), patch.object(worker, "record_run") as record:
            worker.run_one("http://fixture.invalid",
                           self._claimed(), self.args)
        self.assertIn("CARLA boot timeout", upload.call_args.args[2])
        record.assert_not_called()

    def test_recorder_version_system_exit_is_submitted_as_infrastructure_error(self):
        with patch.object(worker, "fetch_package", return_value={"runnable": True, "map": {"name": "Town05"}}), \
                patch.object(worker, "ensure_carla"), patch.object(worker, "mark_running"), \
                patch.object(worker, "record_run", side_effect=SystemExit("CARLA version mismatch")), \
                patch.object(worker, "put_telemetry", return_value={"verdict": "ERROR", "outcome": "INFRA_FAILED"}) as upload:
            worker.run_one("http://fixture.invalid",
                           self._claimed(), self.args)
        self.assertIn("CARLA version mismatch", upload.call_args.args[2])

    def test_unrunnable_package_is_reported_without_starting_the_simulator(self):
        with patch.object(worker, "fetch_package", return_value={"runnable": False, "map": {"name": "Town05"}, "issues": ["missing map metadata"]}), \
                patch.object(worker, "ensure_carla") as ensure, patch.object(worker, "mark_running"), \
                patch.object(worker, "record_run") as record, \
                patch.object(worker, "put_telemetry", return_value={"verdict": "ERROR", "outcome": "INFRA_FAILED"}) as upload:
            worker.run_one("http://fixture.invalid",
                           self._claimed(), self.args)
        self.assertIn("missing map metadata", upload.call_args.args[2])
        ensure.assert_not_called()
        record.assert_not_called()

    def test_mixed_map_runs_keep_their_own_packages_and_recording_options(self):
        self.args.lite = True
        self.args.max_sim_s = 40.0
        telemetry = self.root / "recording.jsonl"
        telemetry.write_text(
            '{"kind":"end","outcome":"TRUNCATED","t":40}\n', encoding="utf-8")
        packages = [{"runnable": True, "map": {"name": name}}
                    for name in ("Town01", "Town01_Opt", "Town10HD")]
        with ExitStack() as stack:
            stack.enter_context(patch.object(
                worker, "fetch_package", side_effect=packages))
            ensure = stack.enter_context(patch.object(worker, "ensure_carla"))
            stack.enter_context(patch.object(worker, "mark_running"))
            record = stack.enter_context(patch.object(
                worker, "record_run", return_value=(str(telemetry), None)))
            stack.enter_context(patch.object(worker, "put_telemetry", return_value={
                                "verdict": "PASS", "outcome": "TRUNCATED"}))
            for index in range(3):
                worker.run_one("http://fixture.invalid",
                               self._claimed(str(index)), self.args)
        self.assertEqual([call.args[0]["map"]["name"] for call in record.call_args_list], [
                         "Town01", "Town01_Opt", "Town10HD"])
        self.assertEqual(set(call.kwargs["boot_map"] for call in ensure.call_args_list), {
                         "Town01", "Town01_Opt", "Town10HD"})
        for index, call in enumerate(record.call_args_list):
            self.assertEqual(call.kwargs["seed"], 73)
            self.assertEqual(call.kwargs["run_id"], f"run-{index}")
            self.assertEqual(call.args[1], "cautious")
            self.assertIs(call.args[2], self.args)
            self.assertTrue(call.args[2].lite)
            self.assertTrue(call.args[2].no_video)
            self.assertEqual(call.args[2].max_sim_s, 40.0)

    def test_recorder_child_manifest_preserves_package_options_and_result_paths(self):
        self.args.lite = True
        self.args.no_video = False
        self.args.max_sim_s = 40.0
        self.args.port = 2010
        package = {"map": {"name": "Town01_Opt"}, "resolvedContent": {"execution": {
            "timeout_s": 120, "warmup_s": 2, "fixed_delta_seconds": 0.05}}, "contentHash": "original-hash"}
        paths = {"telemetry": str(
            self.root / "original.jsonl"), "video": str(self.root / "original.mp4")}
        observed = {}

        def child(command, **kwargs):
            observed["command"] = command
            observed["kwargs"] = kwargs
            observed["manifest"] = json.loads(
                Path(command[-2]).read_text(encoding="utf-8"))
            Path(command[-1]).write_text(json.dumps(paths), encoding="utf-8")
            return SimpleNamespace(returncode=0)

        with patch.object(worker.subprocess, "run", side_effect=child):
            result = worker.record_run(
                package, "cautious", self.args, run_id="recorded-run", seed=73)
        self.assertEqual(result, (paths["telemetry"], paths["video"]))
        self.assertEqual(observed["manifest"]["package"], package)
        options = observed["manifest"]["options"]
        self.assertEqual(options.pop("sut_type"), "behavior_agent")
        self.assertEqual(Path(options.pop("progress_path")).name, "progress.json")
        self.assertEqual(options, {"profile": "cautious", "out_dir": os.path.abspath(self.args.out), "host": "localhost", "port": 2010, "tm_port": 8000,
                                   "run_id": "recorded-run", "seed": 73, "record_video": True, "lite": True, "max_sim_s": 40.0})
        self.assertEqual(observed["command"][0], sys.executable)
        self.assertEqual(
            Path(observed["command"][2]).name, "carla_record_job.py")
        self.assertEqual(observed["kwargs"]["cwd"], worker.ROOT)
        self.assertGreaterEqual(observed["kwargs"]["timeout"], 180)

    def test_native_recorder_exit_without_result_is_a_runtime_error(self):
        for returncode in (0, 1, 3221225477):
            with self.subTest(returncode=returncode), patch.object(worker.subprocess, "run", return_value=SimpleNamespace(returncode=returncode)):
                with self.assertRaisesRegex(RuntimeError, "without a recording result"):
                    worker.record_run({}, "normal", self.args)

    def test_recorder_child_error_result_is_a_runtime_error(self):
        def child(command, **_kwargs):
            Path(command[-1]).write_text(json.dumps(
                {"error": "CARLA fixture sensor failed"}), encoding="utf-8")
            return SimpleNamespace(returncode=1)

        with patch.object(worker.subprocess, "run", side_effect=child):
            with self.assertRaisesRegex(RuntimeError, "fixture sensor failed"):
                worker.record_run({}, "normal", self.args)

    def test_nonzero_child_cannot_return_a_successful_recording(self):
        def child(command, **_kwargs):
            Path(command[-1]).write_text(json.dumps(
                {"telemetry": "recording.jsonl", "video": None}), encoding="utf-8")
            return SimpleNamespace(returncode=3221225477)

        with patch.object(worker.subprocess, "run", side_effect=child):
            with self.assertRaisesRegex(RuntimeError, "3221225477"):
                worker.record_run({}, "normal", self.args)

    def test_child_watchdog_timeout_reports_infra_to_the_backend(self):
        with patch.object(worker, "fetch_package", return_value={"runnable": True, "map": {"name": "Town05"}}), \
                patch.object(worker, "ensure_carla"), patch.object(worker, "mark_running"), \
                patch.object(worker.subprocess, "run", side_effect=subprocess.TimeoutExpired("fixture recorder", 180)), \
                patch.object(worker, "put_telemetry", return_value={"verdict": "ERROR", "outcome": "INFRA_FAILED"}) as upload:
            worker.run_one("http://fixture.invalid",
                           self._claimed(), self.args)
        self.assertIn("INFRA_FAILED", upload.call_args.args[2])
        self.assertIn("timed out", upload.call_args.args[2])


if __name__ == "__main__":
    unittest.main()
