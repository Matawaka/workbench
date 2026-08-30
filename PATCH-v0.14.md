# Matawaka Workbench v0.14 — self-hosted GUI update loop acceptance

v0.14 is the first Workbench candidate intentionally designed to be reached without an external source-apply/bootstrap step. It does not widen the authority surface; it validates that the already-separated v0.10-v0.13 gates can carry a successor end to end.

Target sequence:

`validated local package`
→ `explicit staging materialization`
→ `read-only staged Add/Replace/NoOp plan`
→ `explicit exact source apply + fixed offline build/publish`
→ `separate exact candidate launch`
→ `Self-test`
→ `separate local checkpoint`

## Authority remains non-transitive

Every transition must be freshly revalidated and explicitly authorized where it has effects. A successful earlier receipt is evidence for the next gate, not authority for it.

`Package validity != Materialization authority != Source apply/build authority != Launch authority != Checkpoint authority`

## Stable package format

The bounded package manifest remains `matawaka.workbench-update-package/v0.10`. v0.14 changes maintenance receipt versions, not the package-format contract.

## Build boundary

The build gate remains fixed to `<workspace>/.dotnet-sdk/dotnet.exe` and fixed `build/publish --no-restore` operations. This requests no package restore or network operation, but does not claim OS network isolation.

## Non-effects

The GUI update loop does not create Git remote publication authority, Matawaka catalog mutation authority, Agent Execute/ActionPermit authority, Stable Core promotion, or an OS sandbox claim.

v0.14 acceptance is a Workbench-local proof that the candidate passed the semantic acceptance matrix and a separate local checkpoint decision. The maintenance receipts remain distinct evidence of how that candidate was reached.

## Maintenance causal liveness

The updater itself is part of the causal boundary. A successor payload cannot
repair code that is already executing in the predecessor process, so the
v0.13->v0.14 transition uses a separately repaired predecessor maintenance
runtime. In v0.14 the repair becomes part of the accepted source.

Read-only Git and fixed local dotnet subprocesses are asynchronous and
timeout-bounded. On timeout Workbench requests termination of the exact child
process tree and enters the existing rollback path. This preserves a bounded
failure surface without widening network, Git publication, catalog, Agent
Execute, launch, or checkpoint authority.
