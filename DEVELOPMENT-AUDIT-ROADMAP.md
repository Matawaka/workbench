# Matawaka Workbench — Development Audit & Roadmap

## Current accepted frontier

Accepted and remotely published predecessor:

- commit `df211d1f4d80d0b1f238f1166460758e73ce18d2`;
- tag `workbench-v0.33-accepted`;
- parent `24c98787817b3b37f1a7197ecb5627be130f2581`;
- tree `ea1fde5f211534f1293e3bb53a594ba612b647ed`.

v0.34 candidate source does not change that frontier before explicit local acceptance and separate publication.

## What is now established

Historical work through v0.31 established bounded analysis, semantic/runtime separation, liveness, local acceptance, recovery/transport/key evidence boundaries and a self-hosted update loop.

v0.32 converted that evidence-development line into a cleaner product surface and accepted a fixed fast-forward-only GitHub publisher.

v0.33 accepted/published **Maintenance Update Orchestrator**:

```text
Update candidate
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
```

The one Update candidate session preserves existing typed plan/materialize/staged-plan/apply-build receipts and rollback behavior.

## v0.34 — Maintenance Lifecycle Receipt

Goal: make the already-separated lifecycle auditable as one exact relation without creating another action authority.

Post-publication target:

```text
existing orchestrator/build evidence
+ exact passing Self-test artifact
+ exact local checkpoint receipt
+ exact publication receipt
+ current local HEAD/tag/clean state
-> Lifecycle assessment
-> explicit local Lifecycle receipt evidence write
```

### Required bindings

- checkpoint at current accepted v0.34 HEAD/tag;
- acceptance artifact path is taken from checkpoint, not guessed;
- acceptance artifact SHA-256 equals checkpoint binding;
- acceptance executable SHA-256 equals checkpoint executable SHA-256;
- unique orchestrator receipt targets v0.34 and its built candidate executable SHA-256 equals that same Self-test executable;
- unique publication receipt binds exact predecessor/current accepted commit and exact remote main/tag;
- every consumed artifact receives an explicit SHA-256 binding;
- local source tree is clean.

### Required refusals

- no qualifying artifact → fail;
- more than one qualifying artifact → fail;
- artifact digest drift → fail;
- current HEAD/tag mismatch → fail;
- executable digest discontinuity → fail;
- publication/current accepted commit mismatch → fail.

`Latest File != Correct File Without Exact Binding`

### Authority boundary

```text
Lifecycle Summary != Authority
Observed Sequence != Authorized Sequence
Receipt Binding != Automatic Transition
Missing/Ambiguous Evidence != Inferred Success
Publication Success != Retroactive Update Authority
Lifecycle Receipt != ActionPermit
```

The lifecycle service may perform only local artifact reads/hashes, fixed read-only Git observations and an explicitly confirmed local evidence write. It does not call update/build/launch/Self-test/checkpoint/publication/retry/rollback actions.

## Post-v0.34 decision gate — Maintenance Lifecycle Qualification

Do **not** assume an automatic v0.35 feature layer.

After v0.34 is accepted/published, the next useful evidence is a real successor transition using accepted v0.34:

1. use **Update candidate** on a new bounded successor package;
2. complete separate Launch/Self-test/Accept/Publish;
3. run **Lifecycle receipt**;
4. determine whether exact lifecycle binding succeeds without manual reconciliation.

Possible outcomes:

- `LIFECYCLE_REUSABLE` — composition works as a stable product audit capability;
- `LIFECYCLE_NEEDS_ADAPTER` — one existing receipt lacks enough direct binding;
- `LIFECYCLE_AMBIGUOUS` — repeated artifacts require a stronger identity key;
- `LIFECYCLE_NOT_REQUIRED` — individual receipts are already sufficient and aggregate adds little value.

A negative result is acceptable and should block feature inflation.

## Later research directions

Keep separate until independent evidence requires them:

- cross-machine/cross-OS portability;
- trusted producer identity/certificate/trust-anchor models;
- trusted time and real key revocation policy;
- secure external distribution/signing;
- deeper Workbench ↔ UU-AAP reusable composition only where consumer demand is demonstrated.

## Architecture discipline

```text
Product utility != Core requirement
Historical proof != Permanent UI obligation
UI consolidation != Evidence erasure
Maintenance automation != Authority collapse
Accepted local state != Published remote state
Publication capability != General network capability
Lifecycle observability != Lifecycle authority
```
