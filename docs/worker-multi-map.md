# Running campaigns across the installed CARLA maps

The worker follows the map in each run's immutable execution package. A campaign
can contain different maps; the launcher no longer selects only Town10 campaigns.
No campaign ID means that the worker polls the backend queue until stopped.

The supported CARLA 0.9.16 package contains these twelve scenario maps. The
launcher checks the local map assets before claiming work:

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
# From this Backend checkout. Select the installation explicitly if needed.
$env:CARLA_ROOT = "<CARLA installation directory>"
$env:ASX_API = "http://localhost:5080"
.\tools\run_worker.ps1
```

The launcher chooses an existing Python environment and starts
`tools\campaign_worker.py`. It does not start the .NET API
or install packages. CARLA is always started with Low quality. A run uses its own
scenario timeout; "Full" does not impose a fixed ninety-second duration.

The launcher uses `--carla-root` or `CARLA_ROOT` when set. Otherwise it searches
the current user's Downloads and the checkout's ancestor directories for
`CARLA\CARLA_0.9.16` or `CARLA_0.9.16`. No drive or user name is fixed in the launcher.
`ASX_PYTHON` can select an existing Python executable. Otherwise it tries
`CARLA_ROOT\ScenarioRunner\.venv\Scripts\python.exe`, the repository `.venv`,
`py -3.12`, then an installed `python.exe`. Using an environment inside the
ScenarioRunner directory does not mean the worker invokes ScenarioRunner.

For Supabase authentication set `ASX_ACCESS_TOKEN` to a worker-authorized access
token. A token uses the Bearer header and takes precedence over development role
headers. The `ASX_DEV_ROLE` header fallback is only for an API configured with
development authentication; it defaults to `TEST_ENGINEER`.

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
The recorder supports the five v1 event types and five trigger types, and only
BehaviorAgent/Autopilot Ego controllers. Unsupported synchronized arrival
(`sync`) and lead-vehicle `time_gap_s` controls fail explicitly. Actor/event IDs,
actor types, missing dependencies and cyclic `after_event` chains are checked
before a simulator client is created. A successfully applied event fires once;
dependent delays use that action's actual start time. TTC requires finite
observed positions and velocities and uses the schema's radial closing-speed
definition. An untriggered required action cannot produce a completed scenario.

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
python -m unittest discover -s tools -p test_campaign_worker.py -v
python -m unittest discover -s tools -p test_execution_runtime.py -v
# This file uses its own entry point; unittest discovery alone runs none of its checks.
python tools/test_carla_recorder.py
```

Optional real map smoke checks use a separate simulator on RPC 2010. They do not
claim campaign jobs, call the backend or write the database. They spawn the Ego,
run BehaviorAgent briefly, check that Ego moves and save original ticks. `TRUNCATED` means a short map
smoke recording, not a PASS verdict for the scenario.

```powershell
& "$env:CARLA_ROOT\ScenarioRunner\.venv\Scripts\python.exe" tools/smoke_carla_maps.py --carla-root "$env:CARLA_ROOT"
```

The commands above describe how to verify a particular installation. Passing
offline tests does not establish real simulator acceptance. Record the output
of real map/campaign checks separately, including each map, Oracle result and
video/telemetry artifacts. Fixture mode (`--fixture`) does not start CARLA; it
uploads the supplied telemetry to the selected API and is not real acceptance.
