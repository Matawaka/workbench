# Workbench v0.45 — active surface contract + legacy-control quarantine

## Accepted predecessor

- commit: `2877b4bf95143780edff19231eedf57520bd57ec`
- tag: `workbench-v0.44.1-accepted`
- semantic predecessor: `0.44.1`

## Product intent

v0.45 does not add a new operator capability. It makes the already-accepted product surface explicit and fail-closed:

- exactly four normal maintenance actions remain visible: `Update Workbench`, `Local apps`, `Publish accepted`, `Lifecycle receipt`;
- retired manual controls (`Self-test`, `Accept`, `Stop`, `Launch candidate`) remain compatibility bindings only and are runtime-quarantined;
- historical Agent/git-fetch checkboxes are forced unchecked/disabled/collapsed;
- the whole legacy compatibility container remains collapsed, non-hit-testable and non-focusable;
- hidden Workspace/Catalog state remains available internally because accepted bounded services still use it;
- v0.44.1 app tree, double-click text inspection, closable tabs, search and status/progress behavior are preserved.

## Roadmap reconciliation

Permanent roadmap text now describes the actual accepted normal flow:

```text
Update Workbench
-> one explicit confirmation
-> bounded exact source apply/build
-> one-shot candidate launch/handoff
-> successor first-boot validation
-> automatic local Accept only if PASS
-> separate Publish accepted
-> optional Lifecycle receipt
```

The internal candidate/validation/accept stages remain distinct evidence and authority boundaries even though they are not separate normal-workflow buttons.

## Invariants

```text
Historical Capability != Active UI Obligation
Hidden Compatibility Binding != Operator Authority
Internal Stage != Permanent UI Button
Workspace/Catalog Hidden != Workspace/Catalog Undefined
UI Removal != Evidence Erasure
```

## Lifecycle

- semantic Version: `0.45.0`
- target tag: `workbench-v0.45-accepted`
- exact parent: `2877b4bf95143780edff19231eedf57520bd57ec / workbench-v0.44.1-accepted`
- Update Workbench keeps one-confirmation bootstrap semantics;
- Publish accepted remains explicit and separate;
- Lifecycle receipt remains explicit and separate.

## Non-effects

No new application mutation, file write, process execution, network, catalog mutation, Agent Execute, ActionPermit, trust, identity or Stable Core authority. Quarantine changes reachability/presentation of historical controls only.
