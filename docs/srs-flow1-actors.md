# SRS – Flow 1 Actors (draft for B2)

## Actors

| Actor | Role code | Responsibility in Flow 1 |
|---|---|---|
| Scenario Engineer | `SCENARIO_ENGINEER` | Primary actor. Creates, edits, validates, publishes, versions, clones, imports/exports, tags, archives scenarios and generates parameter variants. |
| Administrator | `ADMIN` | Has every Scenario Engineer permission; manages users and system configuration (Flow 5). |
| Test Engineer | `TEST_ENGINEER` | Secondary actor. Reads READY scenario versions, their execution packages and the coverage map to build test campaigns (Flow 2). Cannot modify scenarios. |
| Test Reviewer | `TEST_REVIEWER` | Secondary actor. Reads READY scenario versions and coverage to trace failures back to the scenario version (Flow 4). Cannot modify scenarios. |
| Flow 2 runner | – (system) | Consumes READY versions through `GET /api/v1/scenario-versions/ready` and the execution package API. |

## Note on the Scenario Engineer role

The capstone registration form assigns scenario authoring ("create, edit, clone, import, and organize test
scenarios; configure map, route, weather, traffic, actors, and scenario parameters") to the Test Engineer.
The team split this responsibility into a dedicated **Scenario Engineer** role for two reasons:

1. **Separation of duties.** The person who designs a scenario is not the person who executes campaigns
   against it. A Test Engineer can only select immutable READY versions, so a campaign result is always
   traceable to a scenario version that no campaign participant could have changed.
2. **Clear ownership per flow.** Each flow of the platform has one primary actor; Flow 1 (Scenario
   Management & Configuration) is owned by the Scenario Engineer.

The functional requirements of the registration form are unchanged: they are delivered by the Scenario
Engineer (authoring) and the Test Engineer (selection and execution) together. One person may hold both
roles in a deployment by switching accounts, and an Administrator can perform either.

## Access summary

| Capability | Scenario Engineer | Administrator | Test Engineer | Test Reviewer |
|---|:-:|:-:|:-:|:-:|
| Create / edit draft / validate / publish | ✓ | ✓ | – | – |
| Clone, import/export, variants, tags, archive/restore | ✓ | ✓ | – | – |
| Version history, diff, activity timeline | ✓ | ✓ | – | – |
| List READY versions, execution package | ✓ | ✓ | ✓ | ✓ |
| Coverage map, map/catalog reference data | ✓ | ✓ | ✓ | ✓ |

Enforced by the `ManageScenarios` policy (`SCENARIO_ENGINEER`, `ADMIN`); every other endpoint only requires
an authenticated user.
