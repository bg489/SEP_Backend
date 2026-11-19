# AutoScenarioX

AutoScenarioX is a scenario-based testing platform for autonomous driving software, built on the CARLA simulator. Instead of developing a driving AI, the platform tests an existing one (CARLA Behavior Agent / Autopilot) by generating driving scenarios, executing them in simulation, detecting failures with an automated test oracle, explaining those failures, and prioritising the scenarios most likely to reveal faults (APFD/APFDc).

> Capstone project – Software Engineering, FPT University (09/2026 – 03/2027).

```
Create scenario → Run on CARLA → Test Oracle detects failures → Failure analysis & explanation → Scenario prioritisation
```

## Key Features

- **Scenario Repository**: Browse, search and filter scenarios by category, map, status and draft state. Archive, restore and clone scenarios.
- **Six-Step Configuration Wizard**: Configure Map & Route, Weather, Traffic, Ego Vehicle, Events & Parameters, and Execution settings through a guided form, backed by an internal JSON schema (v1.0).
- **Three-Tier Validation**: Every draft is checked at three levels before it can be published: JSON Schema (draft 2020-12), business rules (parameter ranges, actor references, trigger timing, start ≠ goal), and CARLA map constraints (map exists, spawn points valid, NPC capacity).
- **Immutable Versioning**: Publishing creates a READY version with a SHA-256 content hash. Versions cannot be modified, and several READY versions can coexist. Includes version history, side-by-side diff and manual deprecation.
- **Draft Concurrency Control**: One draft per scenario, protected by ETag / `If-Match` optimistic locking.
- **Import / Export**: JSON and YAML adapters, with a pluggable design for CARLA Route XML and OpenSCENARIO.
- **Parameter Variant Generation**: Expand a parameter grid (Cartesian product) from a READY version into validated variant scenarios for experiments.
- **Execution Handoff**: A dedicated API serves READY versions as resolved execution packages (parameters substituted, spawn indices converted to coordinates) for the CARLA runner.
- **Role-Based Access Control**: Supabase Auth (JWT/JWKS) with business roles stored in the database, plus a no-login Dev mode for local development.
- **Audit Trail**: Scenario lifecycle actions are recorded in `audit_logs`: create, draft save/discard, validate, publish, deprecate, archive/restore, clone, import, export and variant generation.

## Project Modules

| Flow | Module | Main Actor | Status |
|---|---|---|---|
| 1 | Scenario Management & Configuration | Scenario Engineer | Done |
| 2 | Test Campaign & CARLA Execution | Test Engineer | Starting |
| 3 | Telemetry & Automated Test Oracle | System | In progress (needs migration 105) |
| 4 | Failure Analysis & Explainability | Test Reviewer | Planned |
| 5 | Administration (users, CARLA config, oracle thresholds, dashboard) | Administrator | Planned |

## Tech Stack

### Frontend
The frontend lives in a separate repository maintained by the team's frontend members: <https://github.com/bg489/SEP_Frontend>. This repository contains the backend only.

### Backend
- **Framework**: ASP.NET Core Web API (.NET 8), 3-layer architecture (Controllers → Services → Repositories / UnitOfWork)
- **Database**: Supabase (PostgreSQL), dedicated `asx` schema, managed with plain SQL migrations
- **ORM**: Entity Framework Core 8 + Npgsql, snake_case naming conventions
- **Validation**: JsonSchema.Net (JSON Schema draft 2020-12) + custom business and CARLA rules
- **Formats**: System.Text.Json, YamlDotNet
- **Auth**: JWT Bearer (Supabase JWKS)
- **API Docs**: Swagger (Swashbuckle)
- **Testing**: xUnit, NSubstitute

### Tooling
- **Python 3**: Reference validator, validator comparison harness, CARLA map metadata export, and API test scripts in `tools/`
- **CARLA 0.9.16**: Spawn point metadata for 12 maps in `map_metadata/`

## Project Structure

```text
AutoScenarioX/
├── backend/                ASP.NET Core Web API (.NET 8)
│   ├── AutoScenarioX.sln
│   ├── AutoScenarioX.Api/         Controllers → Services → Repositories
│   └── AutoScenarioX.Api.Tests/   xUnit unit tests (offline)
├── database/migrations/    SQL scripts run on Supabase (no EF Migrations)
├── examples/               Sample scenarios shared by C# and Python tests
├── map_metadata/           CARLA 0.9.16 spawn points for 12 maps
├── tools/                  Python reference validator and test scripts
└── docs/                   Schema, Flow 2 handoff contract, ERD
```

## Prerequisites

- .NET 8 SDK (`dotnet --version` must start with `8.`; do not upgrade to 9/10)
- Python 3.10+ (only for the scripts in `tools/`)
- A Supabase connection string. The team shares one dev database, so ask the team lead for it privately.
- Rider (recommended) or VS Code

## Getting Started

### 1. Clone the repository

```sh
git clone https://github.com/huylh14/AutoScenarioX.git
cd AutoScenarioX
```

### 2. Backend Setup

Store the connection string in .NET user-secrets. They live outside the repo (`~/.microsoft/usersecrets/`), so they are never committed:

```sh
cd backend
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=<host-pooler>;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<password>;SSL Mode=Require" --project AutoScenarioX.Api
dotnet build
dotnet run --project AutoScenarioX.Api --launch-profile http
```

Replace the whole quoted value with the connection string the team lead sends you privately. Do not fill in the placeholders yourself.

The API will be available at <http://localhost:5080/swagger>. Call `GET /api/health/db`; a healthy setup returns `status: OK` and `asxTables: 18`.
The API will be available at <http://localhost:5080/swagger>. Call `GET /api/health/db`; a healthy setup returns `status: OK` and an empty `missingTables`. Tables of other flows in the `asx` schema are ignored.

In Rider, open `backend/AutoScenarioX.sln` and press Run. The `https` profile serves <https://localhost:7080> and also serves HTTP on port 5080.

### 3. Frontend Setup

Clone and run the frontend from <https://github.com/bg489/SEP_Frontend> (see its README). Its Vite dev server forwards `/api` to `http://localhost:5080`, so the backend must be running.

By default the backend runs in **Dev auth mode**, which signs you in automatically as a seeded Scenario Engineer. The frontend's `VITE_AUTH_MODE` must match. To use real Supabase login, run the backend with `Auth:Mode=Supabase`, set `Auth:AnonKey` in user-secrets, and set `VITE_AUTH_MODE=Supabase` in the frontend.

### 4. Run the tests

```sh
cd backend && dotnet test     # unit tests, offline, no database needed
```

## Configuration

The backend does not use `.env` files. Settings come from `appsettings.json` → `appsettings.Development.json` → user-secrets → environment variables, and later sources override earlier ones.

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Supabase` | (empty) | **Required**, secret |
| `Auth:Mode` | `Dev` | `Dev` or `Supabase`; must match the frontend's `VITE_AUTH_MODE`. `Dev` only starts in the Development environment |
| `Auth:ProjectUrl` | (empty) | Supabase project URL, required when `Mode=Supabase` |
| `Auth:AnonKey` | (empty) | Secret, required when `Mode=Supabase` |
| `Auth:AutoProvisionUsers`, `Auth:DefaultRole` | `true`, `SCENARIO_ENGINEER` | Creates an `asx.app_users` row for new users on first login |
| `DevAuth:UserId`, `DevAuth:Role` | seeded user, `SCENARIO_ENGINEER` | Used only when `Mode=Dev` |
| `Cors:AllowedOrigins` | `[]` | Frontend origins (not needed with the Vite proxy) |
| `Scenario:MaxVariantsPerBatch` | `100` | Upper limit for variant generation |

For deployment, use environment variables with `__` instead of `:`, for example `ConnectionStrings__Supabase=...` and `Auth__Mode=Supabase`.

## Database Migrations

The schema is managed with plain SQL files in `database/migrations/`, run in the Supabase SQL Editor. EF Core Migrations are **not** used.

> ⚠️ **The team shares one dev database that already has the full schema and seed data. Do not re-run these scripts.**
> `000_reset_asx_schema.sql` drops the entire `asx` schema.

To set up a **new, empty** Supabase project, run the scripts in this order:

1. `database/migrations/001_flow1_scenario_management.sql`: tables, views, triggers
2. `database/migrations/100_seed_dev_data.sql`: dev user and sample scenarios
3. `database/migrations/102_import_original_content.sql`
4. `database/migrations/103_ready_view_latest_flag.sql`
5. `database/migrations/104_load_map_metadata_carla_0916.sql`: CARLA map metadata
6. Verify with `database/migrations/900_test_flow1_business_rules.sql`: every row must be `PASS`

To change the schema, add a **new** numbered file (for example `105_flow2_campaigns.sql`) in your pull request. Never edit a script that has already been run. Scripts are applied to the shared database only after the pull request is merged.

## Branching Workflow

| Branch | Purpose | Who merges |
|---|---|---|
| `main` | Stable code for demos and milestone submissions | Team lead, from `dev` only |
| `dev` | Integration branch for day-to-day work (default branch) | Anyone, through a reviewed pull request |
| `flowN/<topic>`, `fix/<topic>` | One feature or fix, e.g. `flow2/campaign-runner`, `fix/draft-etag` | Merged into `dev`, then deleted |

```sh
git checkout dev && git pull
git checkout -b flow2/campaign-runner
# ...commit your work...
git push -u origin flow2/campaign-runner   # then open a pull request into dev
```

- Never push directly to `main` or `dev`.
- Before opening a pull request, run `git pull origin dev` and resolve conflicts on your branch. `dotnet build` and `dotnet test` must pass.
- At each milestone, the team lead opens a pull request from `dev` into `main` once everything builds and the tests pass.

## Security Notes

- **Secrets**: Never commit connection strings, passwords or keys. Use `dotnet user-secrets` for the backend; it is stored outside the repository. In the frontend repository, `VITE_*` values are bundled into the client, so never put secrets in them.
- **Row Level Security**: The `asx` schema has RLS enabled with no policies and is not exposed through the Supabase Data API. Only the backend accesses it. Keep it that way.
- **Shared database**: API test scripts in `tools/` write to the shared dev database. Archive any test scenarios you create.
