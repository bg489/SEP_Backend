# The Oracle judges offline from recorded Telemetry, and each judgement is an immutable Evaluation

The runner records Telemetry for every tick: the ego and every actor within 50 m, plus sensor events. The Oracle (C#, in the backend) judges the Run afterwards and stores the result as an Evaluation tied to one immutable Oracle Config version, which the Campaign pins. A Run has no `verdict` column on purpose. Changing a threshold creates a new Oracle Config version, and re-judging a Run adds a new Evaluation without overwriting the old one. This lets admins tune thresholds without re-running CARLA, and it keeps every reported APFD figure reproducible.

## Consequences

- **A Run stops early only on Collision and Red-light running.** These are detected live by CARLA sensors and don't depend on any threshold. Speeding, Freezing, TTC and Route completion depend on thresholds, so they are judged offline over a Run that continues to the goal or the timeout. If a Run stopped on one of those, a later Evaluation with a looser threshold would have no data for the rest of the Run.
- **After stopping, the runner keeps recording for 1 s.** This tail is marked as post-stop so failure analysis can see how the collision played out. The Oracle ignores it.
- **Collision has no intensity threshold**, for the same reason.
- **The Telemetry stays broad on purpose:** every actor within 50 m, every tick. Deciding at recording time which actor is "relevant" would fix a choice that the offline Oracle must remain free to change.
- **A Run whose Run Outcome is INFRA_FAILED is ERROR under every Oracle Config.** The only fix is a new Run.

## Considered Options

- **Online Oracle in the runner:** simplest, but every Verdict would be fixed forever, and a threshold change would need every Run re-executed in CARLA.
- **Overwriting a single Verdict per Run:** published APFD numbers would silently change after a threshold edit.
