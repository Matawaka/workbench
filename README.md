# Matawaka Workbench v0.34 candidate

Windows/.NET 10 WPF control plane for bounded local Matawaka analysis, authority/evidence inspection, self-hosted maintenance, recovery and explicit accepted-source publication.

## Accepted baseline

The currently accepted and remotely published Workbench is:

- commit `df211d1f4d80d0b1f238f1166460758e73ce18d2`;
- tag `workbench-v0.33-accepted`;
- parent `24c98787817b3b37f1a7197ecb5627be130f2581`.

v0.34 candidate source does not change that frontier until it independently traverses `Update candidate → Launch → Self-test → local checkpoint → Publish accepted`.

## Current maintenance UX

Accepted v0.33 already reduced the pre-launch candidate update path to:

`Update candidate → Запустить candidate`

The first button sequences the existing typed intake/materialize/staged-plan/apply-build gates and preserves their individual receipts. Launch remains separate. Self-test, local acceptance and publication remain later explicit actions.

```text
One operator session != One semantic authority
Successful Build != Candidate Launch
Candidate Launch != Self-test
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Publish Authority
```

## v0.34 product change — Maintenance Lifecycle Receipt

v0.34 adds **Lifecycle receipt** as a post-publication audit action.

It does not perform or authorize maintenance. Instead it attempts to prove that already-existing local evidence forms one exact relation:

`Update candidate/build → Self-test → local checkpoint → Publish accepted`

The lifecycle service binds evidence by identity, not by convenient chronology:

- exact v0.34 checkpoint at current accepted HEAD;
- checkpoint-bound acceptance artifact path + SHA-256;
- passing v0.34 Self-test and exact candidate executable SHA-256;
- the unique v0.33 orchestrator receipt targeting v0.34 whose build executable SHA-256 equals that Self-test executable;
- the unique v0.34 publication receipt whose local/remote main/tag equal the checkpoint accepted commit;
- exact SHA-256 for all four consumed artifacts;
- current local HEAD/tag and clean working tree.

Missing or ambiguous evidence fails closed.

```text
Summary != Authority
Observed Sequence != Authorized Sequence
Receipt Binding != Automatic Transition
Missing Artifact != Inferred Success
Artifact Path != Artifact Identity
Latest File != Correct File Without Exact Binding
Lifecycle Receipt != ActionPermit
```

The only effect of **Lifecycle receipt** after a successful read-only assessment is an explicitly confirmed local evidence file under ignored `artifacts/lifecycle`.

## v0.34 acceptance/publication successors

v0.34 preserves every existing boundary:

1. accepted v0.33 **Update candidate** installs/builds the v0.34 source package;
2. **Запустить candidate** remains separate;
3. v0.34 **Self-test** reuses the full v0.33 read-only matrix and adds only offline lifecycle contract/hostile checks;
4. **Принять** creates local `workbench-v0.34-accepted` over exact v0.33 predecessor only;
5. **Publish accepted** remains a separate fixed `Matawaka/workbench` fast-forward/tag network action;
6. **Lifecycle receipt** runs only after those independent artifacts already exist.

No successful stage silently authorizes a later stage.

## Architecture boundary

Workbench remains a product/application layer, not UU-AAP Stable Core. v0.34 does not modify accepted `Runtime`, `Protocol`, `AgentHost`, `Engine`, `Catalog` or `SemanticHost` layers.

It does not create Agent Execute, ActionPermit, catalog mutation authority, general network authority, arbitrary Git remote/history rewrite, retry/rollback authority, canonical UU-AAP conformance or Stable Core/interface-registry promotion.

## After v0.34

No automatic v0.35 feature expansion is assumed. The next decision gate is **Maintenance Lifecycle Qualification**: use the accepted v0.34 maintenance path on a real successor and determine whether the lifecycle composition is reliably reusable, needs correction, or should remain only an audit convenience.
