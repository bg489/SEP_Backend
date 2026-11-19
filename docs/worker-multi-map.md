# Running campaigns across the installed CARLA maps

The worker follows the map in each run's immutable execution package. A campaign
can contain different maps; the launcher no longer selects only Town10 campaigns.
No campaign ID means that the worker polls the backend queue until stopped.

The installation at `C:\Users\admin\Downloads\CARLA_0.9.16` contains these twelve
The installation at `D:\Download\CARLA_0.9.16` contains these twelve
scenario maps:

| Town | Maps |
| --- | --- |
| Town01 | `Town01`, `Town01_Opt` |
| Town02 | `Town02`, `Town02_Opt` |
| Town03 | `Town03`, `Town03_Opt` |
| Town04 | `Town04`, `Town04_Opt` |
| Town05 | `Town05`, `Town05_Opt` |
| Town10 | `Town10HD`, `Town10HD_Opt` |

Map names are exact. `Town01` and `Town01_Opt` are separate choices. Spawn indices
and their metadata must belong to the selected map. These names describe the
installed town maps, not the generated `OpenDriveMap` utility map.

## Start the worker

Start the .NET API on port 5080 and start a campaign from the frontend. Then:

```powershell
cd "C:\Users\admin\Desktop\Hoc Bai\SEP\Project\AutoScenarioX\AutoScenarioX"
.\Start-ASX-Worker.bat
```

The BAT invokes `tools\run_worker.ps1`, which chooses an existing Python
environment and starts `tools\campaign_worker.py`. It does not start the .NET API
or install packages. CARLA is always started with Low quality. A run uses its own
scenario timeout; "Full" does not impose a fixed ninety-second duration.

The launcher uses `CARLA_ROOT` when set, otherwise the installation above.
`ASX_PYTHON` can select an existing Python executable. Otherwise it tries
`CARLA_ROOT\ScenarioRunner\.venv\Scripts\python.exe`, the repository `.venv`,
`py -3.12`, then an installed `python.exe`. Using an environment inside the
ScenarioRunner directory does not mean the worker invokes ScenarioRunner.

```powershell
# Inventory only: reads local map assets, does not claim a run or open CARLA.
.\tools\run_worker.ps1 -ListMaps

# A single campaign; copy the campaign UUID from the frontend.
.\tools\run_worker.ps1 --campaign-id "<campaign UUID>"

# Process at most one queued run, then exit.
.\tools\run_worker.ps1 --campaign-id "<campaign UUID>" --once

# Limit the simulation recording to forty seconds for a smoke test.
.\tools\run_worker.ps1 -Quick
```

Other Python arguments are forwarded. `--api` and `--carla-root` override launcher
defaults. `ASX_FIXED_MAP` inherited from the old launcher is cleared so it cannot
silently pin a new run to Town10. The old `tools\run_worker_town10.ps1` filename
remains a compatibility wrapper for the same multi-map launcher.

When a run finishes, its original telemetry is submitted to the backend Oracle.
The frontend displays the backend result. Supporting a map does not imply that
every event type or SUT option in the scenario schema has been implemented by
the recorder. Check actual telemetry and terminal errors when testing behavior.

## Map switching and verification

The worker reuses the simulator only when its exact map matches the execution
package. When the map changes it restarts this CARLA installation on the selected
RPC port at Low quality, then checks the actual map reported by the Python API.
It never treats a plain town and its `_Opt` variant as interchangeable.

Every recording runs in a fresh Python child process. This prevents native
Traffic Manager and sensor connections from surviving a simulator restart.
Recording options, scenario version, seed, original telemetry and video paths
are passed through unchanged. A crashed or timed-out recorder becomes an
infrastructure error; it never produces a successful verdict by itself.

This Windows Shipping package can ignore the command-line map URL. During boot,
the worker temporarily changes only the four map keys in
`CarlaUE4\Config\DefaultEngine.ini`, preserving other settings and restoring the
original bytes after startup succeeds or fails. A
`DefaultEngine.ini.asx-worker-backup` recovery copy is retained in that folder if
the terminal is forcibly terminated during startup. Saved GPU settings remain Low.

An unknown, missing or unavailable map is reported as `INFRA_FAILED`; another map
is never used as a substitute. Map/package/startup failures after a claim also
send a terminal callback so the run is not left stuck at Claimed or Running.

Offline checks:

```powershell
<<<<<<< HEAD
& "C:\Users\admin\Downloads\CARLA_0.9.16\ScenarioRunner\.venv\Scripts\python.exe" -m unittest discover -s tools -p test_campaign_worker.py -v
& "C:\Users\admin\Downloads\CARLA_0.9.16\ScenarioRunner\.venv\Scripts\python.exe" tools/test_carla_recorder.py
=======
& "D:\Download\CARLA_0.9.16\ScenarioRunner\.venv\Scripts\python.exe" -m unittest discover -s tools -p test_campaign_worker.py -v
& "D:\Download\CARLA_0.9.16\ScenarioRunner\.venv\Scripts\python.exe" tools/test_carla_recorder.py
>>>>>>> thuan
```

Optional real map smoke checks use a separate simulator on RPC 2010. They do not
claim campaign jobs, call the backend or write the database. They spawn the Ego,
run BehaviorAgent briefly, check that Ego moves and save original ticks. `TRUNCATED` means a short map
smoke recording, not a PASS verdict for the scenario.

```powershell
<<<<<<< HEAD
& "C:\Users\admin\Downloads\CARLA_0.9.16\ScenarioRunner\.venv\Scripts\python.exe" tools/smoke_carla_maps.py
=======
& "D:\Download\CARLA_0.9.16\ScenarioRunner\.venv\Scripts\python.exe" tools/smoke_carla_maps.py
>>>>>>> thuan
```

Validated locally on 2026-10-28: all twelve exact maps loaded in sequence at Low
quality on RPC 2010. Each map spawned a real Ego, produced 21 simulation ticks
and recorded measurable movement during a one-second recording. The 27 worker
unit tests and 9 recorder helper checks also passed. These short map checks do
not validate a full campaign, backend callbacks or every scripted event.
