# Future Workbench ↔ KONTUR work — start here

This is the Workbench-side continuity anchor for later KONTUR integration work.

Before implementation, re-observe both repositories and treat all current SHAs as historical frontier evidence, not automatically current truth. Read `KONTUR_INTEGRATION_BACKLOG.md`, this directory and KONTUR's `pilots/kontur-game-companion/workbench-integration/` support files.

## First preferred implementation

Design a new **bounded artifact transfer + local SHA-256 verification capability** that can consume KONTUR's exact LM1 handoff only after fresh human confirmation.

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

## Second preferred implementation

Accept a KONTUR one-shot RequestEnvelope as semantic intent evidence while requiring a separate Workbench CapabilityLease for technical execution. The Workbench layer may strengthen cross-process replay/execution serialization, but it must not become the source of player intent.

## Third preferred implementation

Build a provider-neutral qualification planner that consumes exact-frontier component manifests and affected-conformance metadata. It may derive affected validators but must not execute them without a separate execution authority.

## Permanent rules

`KONTUR Request != Workbench Authority`.

`Artifact Verified != Runtime Activated`.

`Runtime Ready != Model Request Authority`.

`Model Output != Display Authority`.

`Affected != Incompatible`.

Any future executable integration must be additive to current Workbench read/MCP/runtime transaction semantics and must receive its own hostile qualification and real-host closure proof.