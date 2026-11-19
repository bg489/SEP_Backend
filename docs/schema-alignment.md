# Supplied database schema and the active backend

`schema-core.dbml` is the exact 21-table schema supplied on 19 November 2026.
Its 199 columns are the baseline for the active `/api/v1` backend. The old
Identity/JSONB document host is preserved under `legacy/identity-api` and does
not define the active database.

The numbered SQL migrations build all 21 core tables without replacing their
columns. Week 2 additionally requires:

| Extension | Purpose |
|---|---|
| `simulation_workers` | Registration, heartbeat, capacity, lease ownership and lost-worker recovery |
| `system_settings` | Active Oracle default, analytics thresholds and evidence retention policy |
| `similarity_analyses` | Persisted Gower/DTW v1 results for reload and audit |
| Run attempt fields | New run per retry/rerun, `previous_run_id`, `attempt`, `is_current`, worker/progress and upload identity |

The supplied unique `(campaign_id, scenario_version_id)` would prevent keeping
previous runs. Migration 116 replaces it with unique
`(campaign_id, scenario_version_id, attempt)` and a unique partial index on
`(campaign_id, scenario_version_id) WHERE is_current`. There remains exactly one
current attempt while previous telemetry, video and evaluations are retained.

`schema-full.dbml` is the complete 24-table model, including these extensions;
the two repository/READY views are read models, not extra application tables.

CI verifies both directions needed by the application: `CoreSchemaContractTests`
checks every supplied column exists in the EF model with matching nullability
(database-generated values may use required CLR properties after insert);
`tools/verify_schema_contract.py` checks actual PostgreSQL column types,
nullability, primary keys, foreign keys and unique indexes. It executes read-only
SQL against a dedicated `asx_ci_*` database and writes a JSON report. No shared
database reset or credentials are included in source.
