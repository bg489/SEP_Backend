# OpenSCENARIO XML import subset

This adapter is an initial import milestone, not a full ScenarioRunner/OpenSCENARIO implementation. It converts a supported static route into the same canonical JSON used by the existing validate/publish/worker path.

Import `.xosc` or `.osc` using `POST /api/v1/scenarios/import` multipart field `file`; optional `format=openscenario`. The result remains a draft and must be reviewed before publishing. `examples/review2_static_route.xosc` uses published Town05 map metadata positions. Live spawning still depends on the simulator and must be checked separately.

Supported inputs:

- OpenSCENARIO XML 1.0, 1.1 or 1.2 with `FileHeader.description` starting with `CARLA:`. This explicitly selects CARLA left-handed coordinates; the adapter refuses to guess another coordinate convention.
- One of the 12 installed Town01-05 / Town10HD base and `_Opt` maps, referenced by `RoadNetwork/LogicFile`.
- Inline Vehicle/Pedestrian entities with an explicit CARLA blueprint in `name` or `Property name="blueprint"`. The entity with `Property name="role_name" value="hero"`, or the first entity, is Ego.
- Exactly one Init teleport per entity, using `WorldPosition` x/y/z and heading h in radians (converted to canonical yaw in degrees).
- An Ego `AssignRouteAction` with one open route and at least two world-position waypoints. The first vertex must be within 5 m of the teleport position.
- An optional step initial absolute Ego speed (m/s converted to km/h), declared numeric parameters resolved to values, stationary inline NPC actors, and a single simulation-time timeout.

Catalog references, dynamic Story/Act/Maneuver behavior, controllers, non-step speed changes, moving NPC routes, relative/lane/road positions, environment/global changes and other unsupported actions return a clear parse error. They are never silently removed to create a misleading passing run. Use canonical JSON for actor events, weather and reproducible parameter variants.

XML DTD and external entity resolution are prohibited. Registered formats are discovered from actual adapters. OpenSCENARIO is import-only: export JSON/YAML to preserve every canonical field and event. Failed exports do not write a success audit record.

The adapter adds import warnings to canonical source metadata for preset/default values. Existing schema, business and map validation still run after parsing; XML syntax acceptance is not live execution validation.

Reference: [CARLA ScenarioRunner support matrix](https://scenario-runner.readthedocs.io/en/latest/openscenario_support/).
