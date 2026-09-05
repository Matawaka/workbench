# Future Workbench ↔ KONTUR work — start here

This is the Workbench-side continuity anchor for later KONTUR integration work.

Before implementation, re-observe both repositories and treat all current SHAs as historical frontier evidence, not automatically current truth. Read `KONTUR_INTEGRATION_BACKLOG.md`, this directory and KONTUR's `pilots/kontur-game-companion/workbench-integration/` support files.

## Materialized generic prerequisites

Workbench `main` through v0.54.2 now contains separately bounded generic primitives
for artifact acquisition (v0.52), runtime execution (v0.53) and runtime-tree
materialization (v0.54). These capabilities do not themselves consume KONTUR intent.

The v0.55 candidate adds a provider-neutral source/request provenance binding above
the v0.53 execution lease. It hides the inner bearer and preserves restart as loss of
authority. It still creates no KONTUR adapter or model-request authority.

Any KONTUR artifact acquisition must continue to require:

Required properties:

- exact remote repository/revision/file binding;
- exact expected size and SHA-256;
- fixed destination selected/confirmed locally;
- one transfer only;
- hard network byte ceiling;
- no execution of downloaded bytes;
- local hash verification before success;
- receipt bound back to the KONTUR handoff id/source frontier;
- no runtime/model/game/display authority created by success.

Do not reuse the existing read lease as if it already authorizes network/file-write effects. A transfer capability must be a separately designed authority class.

## Next integration frontier

Translate an exact current KONTUR handoff into the generic v0.55 source binding while
requiring a separate Workbench CapabilityLease for technical execution. The
Workbench layer may strengthen cross-process replay/execution serialization, but it
must not become the source of player intent. The later one-shot model request and
bounded output receipt remain a distinct authority class.

## Third preferred implementation

Build a provider-neutral qualification planner that consumes exact-frontier component manifests and affected-conformance metadata. It may derive affected validators but must not execute them without a separate execution authority.

## Permanent rules

`KONTUR Request != Workbench Authority`.

`Artifact Verified != Runtime Activated`.

`Runtime Ready != Model Request Authority`.

`Model Output != Display Authority`.

`Affected != Incompatible`.

Any future executable integration must be additive to current Workbench read/MCP/runtime transaction semantics and must receive its own hostile qualification and real-host closure proof.
