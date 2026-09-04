# Workbench × KONTUR integration preparation

Status: **reference-only integration scaffold / no runtime authority**.

This directory is the Workbench-side landing zone for future KONTUR handoffs. It intentionally contains no live adapter, downloader, process launcher, listener, model invocation, game access or publication logic.

`Integration Scaffold != Runtime Integration`.

`Validated Handoff != Accepted Authority`.

## Ownership split

- KONTUR owns player-facing request semantics, companion policy, response review and exact display permission.
- Workbench owns generic capability/runtime authority, cross-process ownership, bounded local/remote resource corridors and generic receipts.
- UU-AAP provides shared semantic/invariant/provenance references.

## Files

- `KONTUR_ANCHOR.json` — observed cross-project frontier and future intake priorities.
- `CAPABILITY_HANDOFF.schema.json` — mirrored validation contract for future KONTUR capability handoffs.
- `LM1_ARTIFACT_INTAKE.template.json` — Workbench-side expected intake shape for the exact KONTUR LM1 model artifact; it deliberately grants no download/runtime authority.

The current planning backlog remains in `../../KONTUR_INTEGRATION_BACKLOG.md`.

## Intended first implementation

The first integration should remain smaller than a general action/runtime subsystem: one exact artifact transfer + verification corridor bound to the KONTUR LM1 selection.

```text
validated KONTUR handoff
  -> separate fresh human confirmation in Workbench
  -> exact source/revision/file check
  -> exact destination
  -> bounded network bytes
  -> one transfer
  -> local SHA-256
  -> receipt
```

No runtime selection, process start, benchmark, inference, display or game access may be inferred from successful verification.

## Later reuse

Only after the read/MCP/runtime transaction line is stable should Workbench generalize its runtime primitives for KONTUR's future `llama.cpp` provider. The generic lifecycle should preserve:

`Artifact Verified != Runtime Selected != Runtime Started != Endpoint Ready != Benchmark Authorized != Request Authorized`.

This directory should remain import/inert by default. Future executable code must be introduced in a separately reviewed layer with its own authority and hostile qualification.