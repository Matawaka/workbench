# Matawaka Workbench v0.22 — Recovery Evidence Closure

v0.22 adds a read-only evidence-closure surface over the retained bounded recovery evidence chain.

## Evidence chain

The closure binds three already-created artifacts by exact SHA-256 bytes:

1. v0.19 positive isolated recovery drill;
2. v0.20 bounded recovery capability admission, including its exact pointer/hash back to the v0.19 drill;
3. v0.21 isolated recovery negative-control matrix.

A positive closure requires the positive drill to have converged to the same accepted fixture state, the admission to preserve its deliberately narrow scope, and all three negative controls to have refused before creating recovery authority/execution artifacts.

## New invariant

`Positive Evidence + Admission + Negative Controls != Broader Authority`

A closed evidence envelope does not prove production-main-repository recovery, every failure mode, or automatic recovery safety. It does not create recovery execution, rollback, deletion, source mutation, build, checkpoint, network, catalog, Agent Execute, ActionPermit, Stable Core, or interface-registry authority.

## UI

After `workbench-v0.22-accepted`, use **Recovery closure**. The operation is read-only except for its own retained receipt under `artifacts/recovery-closures`.
