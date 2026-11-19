#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Worker Flow 2→3: claim → select exact map → isolated recorder → Oracle callback.

Ví dụ:
  $env:ASX_API = "http://localhost:5080"
  $env:CARLA_ROOT = "C:\\Users\\admin\\Downloads\\CARLA_0.9.16"
  python tools/campaign_worker.py --poll 5 --cooldown 12
  python tools/campaign_worker.py --windowed   # xem cửa sổ Unreal (nặng hơn)
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import carla_recorder  # noqa: E402
from carla_maps import SUPPORTED_MAPS, discover_installed_maps, normalize_map_name, require_installed_map  # noqa: E402

DEFAULT_CARLA_ROOT = r"C:\Users\admin\Downloads\CARLA_0.9.16"


def carla_root(args) -> str:
    return args.carla_root or os.environ.get("CARLA_ROOT") or DEFAULT_CARLA_ROOT


def api_base(args) -> str:
    return (args.api or os.environ.get("ASX_API") or "http://localhost:5080").rstrip("/")


def request(base: str, method: str, path: str, body: bytes | None = None, content_type: str | None = None):
    token = os.environ.get("ASX_ACCESS_TOKEN")
    headers = {"Authorization": f"Bearer {token}"} if token else {
        "X-Asx-Dev-Role": os.environ.get("ASX_DEV_ROLE", "TEST_ENGINEER")}
    if content_type:
        headers["Content-Type"] = content_type
    elif body is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(
        base + path, data=body, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=180) as resp:
            raw = resp.read()
            if resp.status == 204 or not raw:
                return resp.status, None
            ctype = resp.headers.get("Content-Type", "")
            if "json" in ctype:
                return resp.status, json.loads(raw.decode("utf-8"))
            return resp.status, raw
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {path} → {e.code}: {detail}") from e


def claim(base: str, campaign_id: str | None, worker_id=None):
    from urllib.parse import urlencode
    parameters = {key: value for key, value in {"campaignId": campaign_id, "workerId": worker_id}.items() if value}
    q = "?" + urlencode(parameters) if parameters else ""
    status, data = request(base, "POST", f"/api/v1/runs/claim{q}")
    if status == 204:
        return None
    return data


def mark_running(base: str, run_id: str):
    request(base, "POST", f"/api/v1/runs/{run_id}/running")


def register_worker(base, args):
    import socket
    _, registered = request(base, "POST", "/api/v1/workers", json.dumps({
        "name": args.worker_name or f"{socket.gethostname()}:{args.port}", "host": args.host,
        "rpcPort": args.port, "capacity": 1, "carlaVersion": "0.9.16",
        "maps": discover_installed_maps(carla_root(args)),
    }).encode())
    return registered["id"]


def heartbeat(base, worker_id, args):
    return request(base, "POST", f"/api/v1/workers/{worker_id}/heartbeat", json.dumps({
        "maps": discover_installed_maps(carla_root(args)), "error": None,
    }).encode())


def publish_live_progress(base, run_id, progress_path, stop):
    last_frame = None
    while not stop.wait(1):
        try:
            snapshot = json.loads(Path(progress_path).read_text(encoding="utf-8"))
            if snapshot["frame"] != last_frame:
                request(base, "PUT", f"/api/v1/runs/{run_id}/progress", json.dumps(snapshot).encode())
                last_frame = snapshot["frame"]
        except (OSError, ValueError, RuntimeError, KeyError) as error:
            if Path(progress_path).exists():
                print(f"  progress warning: {error}", file=sys.stderr)


def fetch_package(base: str, scenario_version_id: str) -> dict:
    _, data = request(
        base, "GET", f"/api/v1/scenario-versions/{scenario_version_id}/execution-package")
    return data


def put_telemetry(base: str, run_id: str, text: str):
    _, data = request(
        base, "PUT", f"/api/v1/runs/{run_id}/telemetry",
        body=text.encode("utf-8"),
        content_type="application/x-ndjson",
    )
    return data


def put_video(base: str, run_id: str, video_path: str):
    with open(video_path, "rb") as f:
        body = f.read()
    request(base, "PUT",
            f"/api/v1/runs/{run_id}/video", body=body, content_type="video/mp4")


def requeue_infra(base: str, run_id: str):
    _, data = request(base, "POST", f"/api/v1/runs/{run_id}/requeue-infra")
    return data


def telemetry_from_fixture(path: str, run_id: str) -> str:
    text = open(path, encoding="utf-8").read()
    return text.replace("00000000-0000-0000-0000-0000000000f1", run_id)


def simulated_pass_telemetry(run_id: str, duration_s: float = 8.0) -> str:
    """Telemetry tối thiểu COMPLETED — dùng khi CARLA GPU không chạy được (demo Flow 3)."""
    lines = [
        {"kind": "header", "format": "asx.telemetry/1",
            "run_id": run_id, "simulated": True},
    ]
    t = 0.0
    while t <= duration_s:
        lines.append({
            "kind": "frame",
            "t": round(t, 2),
            "ego": {
                "x": 10.0 + t * 4.0, "y": 0.0, "z": 0.5,
                "yaw": 0.0, "speed_mps": 4.0,
                "throttle": 0.3, "brake": 0.0, "steer": 0.0,
            },
            "traffic_light": "green",
        })
        t += 0.5
    lines.append({"kind": "end", "t": duration_s, "outcome": "COMPLETED"})
    return "".join(json.dumps(r, separators=(",", ":")) + "\n" for r in lines)


def carla_ping(host: str, port: int, timeout: float = 5.0) -> bool:
    try:
        sys.path.append(os.path.join(os.environ.get(
            "CARLA_ROOT", ""), "PythonAPI", "carla"))
        import carla
        client = carla.Client(host, port)
        client.set_timeout(timeout)
        client.get_server_version()
        return True
    except Exception:
        return False


def kill_carla(root: str, port: int = 2000):
    """Stop only this installation's simulator on the requested local RPC port."""
    if os.name == "nt":
        prefix = os.path.abspath(root).rstrip("\\/") + os.sep
        quoted = "'" + prefix.replace("'", "''") + "'"
        command = (
            "$prefix=" + quoted + "; $port=" + str(int(port)) + "; "
            "Get-CimInstance Win32_Process | Where-Object { "
            "$_.Name -in @('CarlaUE4.exe','CarlaUE4-Win64-Shipping.exe') -and "
            "$_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) "
            "} | ForEach-Object { "
            "$m=[regex]::Match([string]$_.CommandLine,'-carla-rpc-port[= ]+(\\d+)'); "
            "$actual=if($m.Success){[int]$m.Groups[1].Value}else{2000}; "
            "if($actual -eq $port){Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue} }"
        )
        subprocess.run(
            ["powershell", "-NoProfile", "-Command", command],
            check=False,
        )
    else:
        raise RuntimeError(
            "Automatic simulator restart is supported by this Windows launcher only.")


def _write_low_gpu_config(root: str, boot_map: str = "") -> None:
    """Scalability thấp + (optional) default map = boot_map để khỏi load_world."""
    cfg_dir = os.path.join(root, "CarlaUE4", "Saved",
                           "Config", "WindowsNoEditor")
    os.makedirs(cfg_dir, exist_ok=True)
    gu = os.path.join(cfg_dir, "GameUserSettings.ini")
    body = """[ScalabilityGroups]
sg.ResolutionQuality=25
sg.ViewDistanceQuality=0
sg.AntiAliasingQuality=0
sg.ShadowQuality=0
sg.PostProcessQuality=0
sg.TextureQuality=0
sg.EffectsQuality=0
sg.FoliageQuality=0
sg.ShadingQuality=0

[/Script/Engine.GameUserSettings]
bUseVSync=False
ResolutionSizeX=800
ResolutionSizeY=450
FullscreenMode=1
LastConfirmedFullscreenMode=1
PreferredFullscreenMode=1
FrameRateLimit=20.000000
"""
    try:
        with open(gu, "w", encoding="utf-8") as f:
            f.write(body)
    except OSError as ex:
        print(
            f"  warning: could not write GameUserSettings.ini: {ex}", file=sys.stderr)

    if boot_map:
        # Unreal đọc override trong Saved — boot thẳng TownXX, tránh crash load_world.
        asset = f"/Game/Carla/Maps/{boot_map}.{boot_map}"
        eng = os.path.join(cfg_dir, "Engine.ini")
        eng_body = f"""[/Script/EngineSettings.GameMapsSettings]
EditorStartupMap={asset}
GameDefaultMap={asset}
ServerDefaultMap={asset}
TransitionMap={asset}
"""
        try:
            with open(eng, "w", encoding="utf-8") as f:
                f.write(eng_body)
            print(f"  config default map → {boot_map}")
        except OSError as ex:
            print(
                f"  warning: could not write Engine.ini: {ex}", file=sys.stderr)


def _set_packaged_boot_map(args, root: str, map_name: str):
    """Windows Shipping may ignore URL/INI flags; temporarily set its real config.

    Preserve every unrelated setting and restore the file once boot has finished.
    A recovery copy survives a force-closed terminal.
    """
    path = Path(root) / "CarlaUE4" / "Config" / "DefaultEngine.ini"
    if not path.is_file() or not map_name:
        return
    original = path.read_bytes()
    text = original.decode("utf-8-sig")
    asset = f"/Game/Carla/Maps/{map_name}.{map_name}"
    section = "/Script/EngineSettings.GameMapsSettings"
    keys = ("EditorStartupMap", "GameDefaultMap",
            "ServerDefaultMap", "TransitionMap")

    def replace(match):
        body = match.group(2)
        for key in keys:
            pattern = rf"(?m)^{key}=[^\r\n]*"
            if re.search(pattern, body):
                body = re.sub(pattern, f"{key}={asset}", body)
            else:
                body += f"\n{key}={asset}\n"
        return match.group(1) + body

    pattern = rf"(?ms)(^\[{re.escape(section)}\][^\r\n]*\r?\n)(.*?)(?=^\[|\Z)"
    if re.search(pattern, text):
        updated = re.sub(pattern, replace, text)
    else:
        updated = text + "\n[" + section + "]\n" + \
            "\n".join(f"{key}={asset}" for key in keys) + "\n"
    recovery = path.with_name("DefaultEngine.ini.asx-worker-backup")
    if not recovery.exists():
        recovery.write_bytes(original)
    args._boot_config_restore = (path, original)
    path.write_bytes((b"\xef\xbb\xbf" if original.startswith(
        b"\xef\xbb\xbf") else b"") + updated.encode("utf-8"))


def _restore_packaged_boot_map(args):
    restore = getattr(args, "_boot_config_restore", None)
    if restore is not None:
        path, original = restore
        path.write_bytes(original)
        args._boot_config_restore = None


def start_carla(args, boot_map: str | None = None) -> str:
    root = carla_root(args)
    exe = os.path.join(root, "CarlaUE4.exe")
    if not os.path.isfile(exe):
        raise RuntimeError(f"CarlaUE4.exe not found at {exe}")
    map_name = require_installed_map(
        root, boot_map or args.boot_map) if boot_map or args.boot_map else ""
    _write_low_gpu_config(root, boot_map=map_name)
    _set_packaged_boot_map(args, root, map_name)
    # -dx11: tránh Vulkan shader-compile fatal trên laptop/hybrid GPU.
    flags = []
    if map_name:
        # Unreal's command-line URL is a package path, not an object reference.
        # The dotted form is for INI settings and may silently boot the default town.
        flags.append(f"/Game/Carla/Maps/{map_name}")
    flags += ["-quality-level=Low", "-dx11", "-nosound",
              "-fps=15", f"-carla-rpc-port={args.port}"]
    if args.windowed:
        flags += ["-windowed", "-ResX=800", "-ResY=450"]
    else:
        flags += ["-RenderOffScreen"]
    print(f"  starting CARLA: {exe} {' '.join(flags)}")
    subprocess.Popen([exe, *flags], cwd=root, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    return root


def _wait_map(host: str, port: int, map_name: str, timeout: float = 60.0, fail_on_mismatch: bool = False) -> bool:
    """True nếu server đã ở đúng map (tránh gọi load_world)."""
    if not map_name:
        return True
    expected = normalize_map_name(map_name)
    try:
        sys.path.append(os.path.join(os.environ.get(
            "CARLA_ROOT", ""), "PythonAPI", "carla"))
        import carla  # type: ignore
    except Exception:
        return False
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            client = carla.Client(host, port)
            client.set_timeout(5.0)
            name = client.get_world().get_map().name.replace("\\", "/")
            if normalize_map_name(name) == expected:
                print(f"  map confirmed: {name}")
                return True
            print(f"  waiting map {map_name} (now {name})...")
            if fail_on_mismatch:
                return False
        except Exception as ex:  # noqa: BLE001
            print(f"  waiting map: {ex}")
        time.sleep(3)
    return False


def _carla_is_offscreen() -> bool:
    """True khi CarlaUE4 đang chạy nhưng không có cửa sổ (-RenderOffScreen)."""
    if os.name != "nt":
        return False
    try:
        out = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "(Get-CimInstance Win32_Process -Filter \"Name = 'CarlaUE4.exe'\").CommandLine"],
            capture_output=True, text=True, check=False, timeout=15,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    return "RenderOffScreen" in (out.stdout or "")


def ensure_carla(args, boot_map: str | None = None) -> None:
    if args.fixture:
        return
    root = carla_root(args)
    # Validate the exact map before stopping a running simulator or writing any config.
    map_name = require_installed_map(
        root, boot_map or args.boot_map) if boot_map or args.boot_map else ""
    if carla_ping(args.host, args.port, timeout=3.0):
        if args.windowed and _carla_is_offscreen():
            print("  CARLA is running hidden — restarting with a window...")
            kill_carla(root, args.port)
            time.sleep(2)
        elif map_name and not _wait_map(args.host, args.port, map_name, timeout=15.0, fail_on_mismatch=True):
            print(
                f"  switching CARLA to {map_name} — restarting this simulator at Low quality...")
            kill_carla(root, args.port)
            time.sleep(2)
        else:
            return
    else:
        print("  CARLA not reachable — restarting...")
        kill_carla(root, args.port)
        time.sleep(2)
    try:
        start_carla(args, boot_map=map_name or None)
        deadline = time.time() + args.carla_boot_timeout
        while time.time() < deadline:
            if carla_ping(args.host, args.port, timeout=5.0):
                print("  CARLA ready")
                time.sleep(5)
                if map_name:
                    if _wait_map(args.host, args.port, map_name, timeout=90.0):
                        return
                    raise RuntimeError(
                        f"CARLA booted but its actual map is not {map_name}; the run was not executed.")
                return
            time.sleep(3)
        raise RuntimeError(
            f"CARLA did not become ready within {args.carla_boot_timeout}s")
    finally:
        _restore_packaged_boot_map(args)


def is_infra_result(result: dict) -> bool:
    if not result:
        return True
    if result.get("verdict") != "ERROR":
        return False
    outcome = (result.get("outcome") or "").upper()
    if outcome == "INFRA_FAILED":
        return True
    for ev in result.get("evaluations") or []:
        reason = (ev.get("errorReason") or "").lower()
        if any(k in reason for k in ("simulator", "timeout", "connection", "carla", "spawn failed", "refused")):
            return True
    return outcome == "" and result.get("verdict") == "ERROR"


def record_run(pkg, profile, args, run_id=None, seed=None, sut_type="behavior_agent"):
    """Isolate native CARLA/TM state so a multi-map queue can reuse the worker."""
    options = {"profile": profile, "out_dir": os.path.abspath(args.out), "host": args.host,
               "port": args.port, "tm_port": args.tm_port, "run_id": run_id, "seed": seed,
               "record_video": not args.no_video, "lite": args.lite, "max_sim_s": args.max_sim_s,
               "sut_type": sut_type}
    execution = pkg.get("resolvedContent", {}).get("execution", {})
    duration = float(execution.get("timeout_s", 120))
    if args.max_sim_s:
        duration = min(duration, args.max_sim_s)
    tick = float(execution.get("fixed_delta_seconds", 0.05))
    wall_limit = max(
        180, (duration + float(execution.get("warmup_s", 2)) + 40) / max(0.01, 15 * tick) + 120)
    with tempfile.TemporaryDirectory(prefix="asx-record-job-") as temporary:
        request_path, result_path = Path(
            temporary) / "request.json", Path(temporary) / "result.json"
        progress_path = Path(temporary) / "progress.json"
        options["progress_path"] = str(progress_path)
        request_path.write_text(json.dumps(
            {"package": pkg, "options": options}), encoding="utf-8")
        stop = threading.Event()
        monitor = None
        if run_id and getattr(args, "publish_progress", False):
            monitor = threading.Thread(target=publish_live_progress,
                                       args=(api_base(args), run_id, str(progress_path), stop), daemon=True)
            monitor.start()
        try:
            process = subprocess.run([sys.executable, "-u", os.path.join(ROOT, "tools", "carla_record_job.py"),
                                      str(request_path), str(result_path)], cwd=ROOT, timeout=wall_limit,
                                     creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        finally:
            stop.set()
            if monitor:
                monitor.join(timeout=2)
        if not result_path.is_file():
            raise RuntimeError(
                f"CARLA recorder process exited with code {process.returncode} without a recording result")
        result = json.loads(result_path.read_text(encoding="utf-8"))
        if process.returncode != 0 or result.get("error"):
            raise RuntimeError(result.get(
                "error") or f"CARLA recorder process exited with code {process.returncode}")
        return result["telemetry"], result.get("video")


def run_one(base: str, claimed: dict, args) -> dict:
    run_id = claimed["runId"]
    version_id = claimed["scenarioVersionId"]
    profile = claimed.get("sutBehavior") or args.profile
    retry = claimed.get("infraRetryCount", 0)
    max_retry = claimed.get("maxInfraRetries", args.max_infra_retries)
    print(
        f"claimed {run_id}  version={version_id}  profile={profile}  infraRetry={retry}/{max_retry}")

    boot_map = None
    video_path = None
    if args.simulate:
        mark_running(base, run_id)
        print("  status → Running")
        text = simulated_pass_telemetry(run_id)
        print("  SIMULATED telemetry (no CARLA) — GPU/infra workaround")
    elif args.fixture:
        ensure_carla(args)
        mark_running(base, run_id)
        print("  status → Running")
        text = telemetry_from_fixture(args.fixture, run_id)
        print(f"  fixture {args.fixture}")
    else:
        # All map/package/startup failures after claim must produce a terminal
        # callback rather than leaving the run permanently Claimed/Running.
        try:
            pkg = fetch_package(base, version_id)
            if not pkg.get("runnable"):
                raise RuntimeError("Execution package not runnable: " +
                                   "; ".join(pkg.get("issues") or ["no reason supplied"]))
            boot_map = require_installed_map(carla_root(
                args), (pkg.get("map") or {}).get("name"))
            ensure_carla(args, boot_map=boot_map)
            mark_running(base, run_id)
            print(f"  status → Running on {boot_map}")
            jsonl_path, video_path = record_run(
                pkg, profile, args, run_id=run_id, seed=claimed.get("seed"),
                sut_type=claimed.get("sutType") or "behavior_agent")
            with open(jsonl_path, encoding="utf-8") as stream:
                text = stream.read()
            print(f"  recorded {jsonl_path}")
        except (Exception, SystemExit) as ex:
            text = carla_recorder.infra_failed_telemetry(run_id, str(ex))
            print(f"  CARLA failed → INFRA_FAILED: {ex}", file=sys.stderr)
            video_path = None

    result = put_telemetry(base, run_id, text)
    verdict = result.get("verdict") if isinstance(result, dict) else None
    print(f"  verdict={verdict}")

    if video_path and os.path.isfile(video_path) and verdict in ("PASS", "FAIL", "ERROR"):
        try:
            put_video(base, run_id, video_path)
            print(f"  uploaded video {video_path}")
        except Exception as ex:  # noqa: BLE001
            print(f"  warning: video upload failed: {ex}", file=sys.stderr)

    if isinstance(result, dict) and is_infra_result(result):
        if retry < max_retry:
            try:
                rq = requeue_infra(base, run_id)
                print(
                    f"  re-queued infra (retry {rq.get('infraRetryCount')}/{rq.get('maxInfraRetries')})")
                # The next claim retries startup; do not abort the loop here.
            except Exception as ex:  # noqa: BLE001
                print(f"  requeue failed: {ex}", file=sys.stderr)
        else:
            print(f"  infra retries exhausted ({retry}/{max_retry})")

    return result if isinstance(result, dict) else {}


def main():
    ap = argparse.ArgumentParser(
        description="AutoScenarioX campaign worker (claim → CARLA → oracle → video).")
    ap.add_argument(
        "--api", help="API base (default env ASX_API or http://localhost:5080)")
    ap.add_argument("--campaign-id", help="Only claim runs of this campaign")
    ap.add_argument("--worker-name", default="", help="Persisted worker name; default is this computer and RPC port")
    ap.add_argument("--no-live-progress", action="store_true", help="Disable live progress snapshots (final telemetry remains required)")
    ap.add_argument("--once", action="store_true",
                    help="Process at most one run then exit")
    ap.add_argument("--poll", type=float, default=5.0,
                    help="Seconds between claim polls when idle")
    ap.add_argument("--cooldown", type=float, default=12.0,
                    help="Seconds after each run before next map load")
    ap.add_argument("--max-infra-retries", type=int, default=2)
    ap.add_argument("--carla-root", default=os.environ.get("CARLA_ROOT") or DEFAULT_CARLA_ROOT,
                    help="CARLA install dir (or set CARLA_ROOT)")
    ap.add_argument("--carla-boot-timeout", type=float, default=120.0)
    ap.add_argument("--windowed", action="store_true",
                    help="Show CARLA window (heavier). Default is -RenderOffScreen.")
    ap.add_argument("--lite", action="store_true",
                    help="Weak GPU: no_rendering_mode, no walkers, cap NPC (no chase video).")
    ap.add_argument("--boot-map", default="",
                    help="Optional initial-map hint. Each claimed run still selects its exact package map.")
    ap.add_argument("--fixed-town", default="",
                    help="Legacy alias for --boot-map; it no longer excludes other scenario maps.")
    ap.add_argument("--list-maps", action="store_true",
                    help="List supported maps installed in CARLA_ROOT without starting CARLA or contacting the API.")
    ap.add_argument("--no-video", action="store_true",
                    help="Skip RGB video capture")
    ap.add_argument("--max-sim-s", type=float, default=None,
                    help="Cap each scenario sim time (seconds). For quick/smoke tests.")
    ap.add_argument("--quick", action="store_true",
                    help="Smoke mode: max-sim 40s, cooldown 3s (real CARLA + video).")
    ap.add_argument("--fixture", help="Skip CARLA; upload this JSONL")
    ap.add_argument("--simulate", action="store_true",
                    help="No CARLA: upload synthetic COMPLETED telemetry (demo when GPU crashes).")
    ap.add_argument(
        "--profile", choices=["cautious", "normal", "aggressive"], default="normal")
    ap.add_argument("--out", default="recordings")
    ap.add_argument("--host", default="localhost")
    ap.add_argument("--port", type=int, default=2000)
    ap.add_argument("--tm-port", type=int, default=8000)
    args = ap.parse_args()
    os.environ["CARLA_ROOT"] = carla_root(args)
    if args.list_maps:
        installed = discover_installed_maps(carla_root(args))
        for name in SUPPORTED_MAPS:
            print(f"{name}: {'installed' if name in installed else 'MISSING'}")
        print(
            f"{len(installed)}/{len(SUPPORTED_MAPS)} supported maps installed at {carla_root(args)}")
        return
    if not 1 <= args.port <= 65535:
        ap.error("--port must be between 1 and 65535")
    if args.poll <= 0 or args.cooldown < 0 or args.carla_boot_timeout <= 0:
        ap.error(
            "--poll and --carla-boot-timeout must be positive; --cooldown cannot be negative")
    if args.max_sim_s is not None and args.max_sim_s <= 0:
        ap.error("--max-sim-s must be positive")
    if not args.simulate and not args.fixture:
        installed = discover_installed_maps(carla_root(args))
        print(
            f"Available CARLA maps ({len(installed)}/{len(SUPPORTED_MAPS)}): {', '.join(installed)}")
        if not installed:
            ap.error("No supported CARLA maps are installed")
    if args.quick:
        if args.max_sim_s is None:
            args.max_sim_s = 40.0
        if args.cooldown > 3.0:
            args.cooldown = 3.0
    if not args.no_video:
        # Video chụp cửa sổ CarlaUE4. Offscreen thì không có khung hình để quay.
        args.windowed = True
    # The package, rather than a launcher-wide Town10 setting, selects every run.
    if args.fixed_town or args.boot_map:
        args.boot_map = require_installed_map(
            carla_root(args), args.fixed_town or args.boot_map)
    os.environ.pop("ASX_FIXED_MAP", None)

    base = api_base(args)
    args.publish_progress = not args.no_live_progress
    print(f"worker → {base}")
    if args.simulate:
        print("  mode: SIMULATE (no CARLA)")
    elif not args.fixture:
        mode = "windowed" if args.windowed else "RenderOffScreen"
        if args.lite:
            mode += "+lite"
        print(f"  CARLA mode: {mode}  cooldown={args.cooldown}s")
        print("  MAP MODE: per scenario; reuse the exact same map, restart at Low quality when it changes")

    worker_id = None
    heartbeat_stop = threading.Event()
    if not args.simulate and not args.fixture:
        worker_id = register_worker(base, args)
        heartbeat(base, worker_id, args)
        def beat():
            while not heartbeat_stop.wait(20):
                try:
                    heartbeat(base, worker_id, args)
                except Exception as error:
                    print(f"worker heartbeat failed: {error}", file=sys.stderr)
        threading.Thread(target=beat, daemon=True).start()
        print(f"  registered worker: {worker_id}")

    while True:
        claimed = claim(base, args.campaign_id, worker_id)
        if claimed is None:
            if args.once:
                print("queue empty")
                return
            time.sleep(args.poll)
            continue
        run_one(base, claimed, args)
        if args.once:
            return
        if args.cooldown > 0:
            print(f"  cooldown {args.cooldown}s before next run...")
            time.sleep(args.cooldown)


if __name__ == "__main__":
    main()
