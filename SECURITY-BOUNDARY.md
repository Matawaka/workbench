# Matawaka Workbench v0.9 — Security boundary

The accepted v0.7 semantic security boundary remains unchanged: fixed verified semantic host, restricted Low-integrity token, Windows Job Object and child runtime attestation before semantic input. Self-test from v0.8 remains read-only/offline.

v0.9 adds one deliberately separate maintenance authority: **explicit human-confirmed local Workbench Git checkpointing**.

## Local checkpoint authority

A local checkpoint is allowed only when all are true:

1. the current process produced a passing Self-test receipt;
2. the acceptance artifact is under `Workbench/artifacts/acceptance` and matches the in-memory receipt;
3. the running Workbench executable hash still matches the Self-test;
4. HEAD equals `workbench-v0.8-accepted`;
5. `workbench-v0.9-accepted` does not already exist;
6. the UI previews the exact working-tree file list;
7. the human explicitly confirms the **Принять** dialog.

Only fixed local operations are available: `git add`, one fixed commit, one fixed annotated tag. There is no remote push/fetch, no catalog write, no JSON-supplied executable/arguments and no agent Execute.

`Self-test PASS != Checkpoint authority`

`Checkpoint authority != Catalog mutation authority`

`Checkpoint authority != Remote publication authority`

`Checkpoint authority != Agent Execute`


## v0.10 source-set and update-intake boundaries

- `Repository HEAD != Relevant Source Set`: unrelated repository movement is observable but does not replace exact bound-file verification.
- Relevant-source verification computes Git blob identity locally from bytes; it performs no fetch and no repository mutation.
- `Update Package Valid != Materialization Authority`: v0.10 only reads/validates a local ZIP and writes a plan receipt under Workbench artifacts.
- ZIP traversal, unmanifested payload files, digest mismatch, oversized payloads and packages requesting network/catalog/Execute/arbitrary-process/installer-script authority are rejected.
- No update payload is extracted or executed in v0.10.
