# Week 2 implementation and verification

Both repositories use `feature/week2-completion-20261119`. The active backend is
`backend/AutoScenarioX.Api` (.NET 8); `legacy/identity-api` preserves the old host
and is not started with the current frontend.

Source was consolidated from the team's existing backend revision `cfa79ed` and
frontend feature commits through `4e5549e`, then corrected and tested on these
branches. `evidence/review2-20261103-historical.json` is historical evidence,
not a new test run.

## Five workflow contracts

| Flow | Actor and input | Persisted output / handoff |
|---|---|---|
| 1: Scenario authoring | Scenario Engineer/Admin; form, JSON/YAML, supported OpenSCENARIO subset, template or clone | Validated immutable READY version, hash, original import and history. Unsupported OpenSCENARIO constructs return errors. |
| 2: Prioritization | Test Engineer/Admin; up to 100 READY versions, weights, seed and Oracle history | Deterministic order; separately labeled estimated/observed APFD/APFDc; persisted Gower/DTW v1 comparison and clusters. |
| 3: Execution/Oracle | Test Engineer/Admin; campaign, Ego agent, versions and Oracle configuration | Atomic run claim, worker lease, progress, telemetry, private video and measured verdict. Linked retries keep old evidence. |
| 4: Failure analysis | Test Reviewer/Admin; measured failures | Persisted group/verify/similar/explain/report/review sequence; returning cases creates new attempts. |
| 5: Administration | Admin; users, workers/capacity, Oracle versions and settings | Database-backed updates, immutable Oracle versions, last-active-Admin protection and audit trail. |

## Reproduce automated checks

Backend, from repository root:

```powershell
dotnet restore backend/AutoScenarioX.Api.Tests/AutoScenarioX.Api.Tests.csproj
dotnet test backend/AutoScenarioX.Api.Tests/AutoScenarioX.Api.Tests.csproj --logger trx --results-directory TestResults
python -m unittest discover -s tools -p test_campaign_worker.py -v
python -m unittest discover -s tools -p test_execution_runtime.py -v
python tools/test_carla_recorder.py
```

Integration tests require `ASX_TEST_DATABASE` pointing to a dedicated PostgreSQL
database named `asx_ci_*`. Apply migrations in order except `000_*` and `900_*`,
then `database/seeds/review2_demo.sql`. Never reset/seed the shared team database.
CI creates a fresh PostgreSQL 18 service and retains TRX/coverage/Python logs.

Frontend:

```powershell
npm ci
npm run lint
npm run build
npm run verify:api
npm run test:coverage
$env:PLAYWRIGHT_CHANNEL = 'chrome' # or install Playwright Chromium
npm run test:e2e
```

Both workflows run on branch pushes, PRs and Review 2 tags. Test/build failures
fail the job. Missing live CARLA evidence is Skipped, not an exercised PASS.

## Isolated review environment

The current local checks use PostgreSQL 18 at `127.0.0.1:55439`, database
`asx_ci_week2` (24 tables and two views), API `http://127.0.0.1:5087` and frontend
`http://127.0.0.1:4175`. These are local review addresses, not published shared
staging. Source revisions, actual outcomes and CI URLs belong in current evidence.

Run the API from `backend/AutoScenarioX.Api`, setting
`ConnectionStrings__Supabase` to the isolated database,
`ASPNETCORE_ENVIRONMENT=Development`, `Auth__Mode=Dev` and a seeded
`DevAuth__UserId`. Set frontend `TEAM_API_TARGET` to that API. Deployments must
use Supabase authentication; Development role selection is only for local tests.

The CARLA root supplied for this run is
`C:/Users/admin/Desktop/Hoc Bai/SEP/CARLA/CARLA_0.9.16`. Other machines use
`CARLA_ROOT`/`--carla-root` or launcher discovery. Quality stays Low; plain and
`_Opt` worlds are distinct, with no silent map substitution.

```powershell
python tools/review2_live_acceptance.py --api http://127.0.0.1:5087 --carla-root "$env:CARLA_ROOT" --port 2017 --out TestResults/carla-live
```

This creates real scenarios/campaigns in the isolated database and saves full
short-route motion, actor/event effects, run/version IDs, telemetry, video and
Oracle verdicts for all twelve maps. Measured FAIL is valid evidence of a found
violation; infrastructure failure, incomplete route or missing video is not
accepted. Do not replace real evidence with fixture/quick/simulated telemetry.

## Demo ownership

| Owner | Demo | Negative case |
|---|---|---|
| Thuận | Draft/save/validate/publish and supported OpenSCENARIO import | Invalid content/stale revision rejected |
| Đạt | Oracle history ranking, Gower/DTW and APFD results | Missing history labeled; invalid weights/IDs rejected |
| Huy | Ego/actors/events, CARLA Low and worker queue | Concurrent claims/retries preserve isolation and evidence |
| Tuấn | Scenario/campaign UI and browser-to-CARLA result reload | API errors/stale replies do not become successful saves |
| Bảo | Reviewer workflow; Admin users, Oracle, workers/settings | Wrong role rejected; last active Admin protected |

Unit invocations, parameterized cases and functional test IDs are counted
separately. Mocked browser tests do not establish live provider/CARLA acceptance.

## External configuration and release

Supabase account/email and cloud-storage acceptance need server-side
`Auth:ProjectUrl`, `Auth:AnonKey`, recovery redirect/email configuration and
`Telemetry:Storage*`. The current user-secrets contain a database connection but
no Auth/Storage keys. Adapters have controlled HTTP tests; live account/email/
bucket checks remain pending until configured. Filesystem artifacts work locally.

Do not overwrite the historical `review-2` tag or report its old success as the
current revision's result. Release tagging requires reviewed source, both required
CI checks and the external acceptance items to be complete.
