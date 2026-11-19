# Backend Review 2 verification

The backend workflow is `.github/workflows/backend-ci.yml`. It runs on pull requests,
branch pushes, `review-2`/`review-2-*` tags and manual dispatch. Jobs fail when build,
unit/HTTP/database tests or worker/recorder regressions fail. Evidence artifacts include
TRX, Cobertura coverage, Python logs, migration logs, exact commit and workflow URL.

## Run locally

From this repository root:

```powershell
dotnet restore backend/AutoScenarioX.Api.Tests/AutoScenarioX.Api.Tests.csproj
dotnet test backend/AutoScenarioX.Api.Tests/AutoScenarioX.Api.Tests.csproj --logger trx --results-directory TestResults --collect:"XPlat Code Coverage"
python -m unittest discover -s tools -p test_campaign_worker.py -v
python tools/test_carla_recorder.py
```

Recorder tests are functions driven by the script's own main entry point. Running
`unittest discover` on that file does not exercise them.

`PostgreSqlReview2Tests` require `ASX_TEST_DATABASE`, an Npgsql connection string to an
isolated PostgreSQL database whose name starts with `asx_ci_`. Apply migrations in sorted
order except `000_reset_asx_schema.sql` and `900_test_flow1_business_rules.sql`, then
`database/seeds/review2_demo.sql`. Never run this bootstrap on the application's existing
Supabase database. The CI uses a fresh PostgreSQL 18 container; no Supabase credentials,
CARLA installation or GPU is needed.

The database checks exercise persistence after reopening a connection, two simultaneous
queue consumers, and linked retry attempts that retain previous telemetry/evaluations.
Missing `ASX_TEST_DATABASE` is reported as **Skipped**, not an exercised database PASS.

The optional live recording check similarly requires `ASX_TELEMETRY_DIR`. Missing live
evidence is **Skipped**. Offline tests and API fixtures do not establish that all twelve
CARLA worlds, actors or events were observed. Keep actual simulator acceptance logs,
run/version IDs, video and telemetry separately. Simulator launches must use Low quality.

## Feature contracts

- `POST /api/v1/prioritization/rank` reads observed PASS/FAIL evaluations and distinct
  failing checks. Infrastructure ERROR does not increase the SUT failure rate. Responses
  include `historyStatus`, observed run/failure counts and nullable observed APFD/APFDc.
  Heuristic estimates remain explicitly labeled; no history is a content-risk fallback.
- `POST /api/v1/prioritization/compare` accepts `scenarioVersionIds`, `method` (`gower` or
  `dtw`) and `clusterThreshold` (0–1). Results are persisted; `GET .../compare/{id}` reloads
  them. Gower v1 covers equally weighted numeric/nominal scenario features. DTW v1 compares
  planned route Cartesian points within the exact same map, resolving spawn indices from
  metadata. It does not claim recorded-trajectory DTW or HDBSCAN: clustering is deterministic
  threshold connected components. Raw DTW meters are reported separately from its bounded
  distance `raw/(1+raw)`.
- All failure-analysis mutations require `ReviewResults` (Reviewer/Admin). Read endpoints
  remain available to authenticated users. Reviewer completion follows the persisted state
  sequence; retries create linked attempts without deleting earlier evidence.
- Admin-only Oracle create/activate and settings endpoints are under `/api/v1/admin`.
  Creating a config allocates a new immutable version; activating it changes only the
  default for new campaigns. Existing campaigns/evaluations retain their config IDs.
- Analytics thresholds and retention days are persisted. Retention is a policy setting;
  this version never automatically deletes referenced run artifacts. `/admin/audit` reads
  actual persisted audit entries with page/pageSize parameters.

Algorithm references: [mixed-feature Gower](https://stat.ethz.ch/R-manual/R-devel/library/cluster/html/daisy.html),
[DTW recurrence](https://tslearn.readthedocs.io/en/stable/user_guide/dtw.html).

## Seed identities

The seed adds four `example.invalid` application-role fixtures. They are not real
Supabase Auth accounts and have no password or service key. In Supabase mode, prepare
actual test identities separately and align their UUIDs with application records. In
Development mode, use the explicitly enabled role controls described in the main README.

Only tag the reviewed source revision after its required checks pass. Report the actual
workflow URL and both repository commits; do not treat creating the YAML file as a CI run.
