# Workbench v0.39.1 — Handoff activation probe

## Purpose

Patch-level successor used to exercise the already-accepted Workbench v0.39 candidate-launch handoff on a real Windows host.

Exact predecessor:

- commit `13f8618c6862b58a9e9de8772c69365058f34e91`
- tag `workbench-v0.39-accepted`

Target:

- semantic version `0.39.1`
- tag `workbench-v0.39.1-accepted`

## Runtime boundary

v0.39.1 does **not** modify:

- `BoundedUpdateApplyBuildService`
- `CandidateLaunchHandoffV039Service`
- `MainWindow.V039.cs`
- Local Apps registration / builder / updater / role guard / receipt store
- Runtime / Protocol / Engine / AgentHost / Catalog / SemanticHost

The top-level `Launch candidate` button remains routed to the accepted `LaunchCandidateV039Button_Click` handler.

The source delta exists only so accepted v0.39 has a legitimate non-empty successor candidate to launch. New files provide v0.39.1 Self-test / local checkpoint / fixed publication routing and this patch note; XAML only changes semantic version and lifecycle handler routing while retaining the v0.39 launch handler.

## Real-host qualification target

When accepted v0.39 launches this candidate:

1. v0.39 writes the existing successful candidate-launch receipt;
2. v0.39 rebinds exact PID/process image through the accepted handoff observer;
3. v0.39 persists `candidate-launch-handoff-v0.39-*.json`;
4. only then v0.39 closes its own predecessor window;
5. v0.39.1 remains open and still requires separate Self-test / Accept / Publish.

Expected handoff status:

`CANDIDATE_ALIVE_PREDECESSOR_SELF_CLOSE_ELIGIBLE_NOT_ACCEPTED`

## Invariants

`Activation Probe != New Runtime Authority`

`Launch != Accept`

`Handoff Receipt Persisted Before Self-Close`

`Predecessor Self-Close != External Process Termination`

## Non-effects

No external process kill/signal authority, no candidate acceptance from launch, no package auto-apply, no network/download, no catalog mutation, no Agent Execute/ActionPermit, no Stable Core/interface-registry change.
