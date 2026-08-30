# Matawaka Workbench v0.2

Status: local Windows integration checkpoint; Execute remains closed.

v0.2 composes the accepted v0.1.3 evidence/authority frontier with the terminal-state work prepared in v0.1.4 and three new bounded surfaces:

1. PCL-compatible visible liveness receipts for current phase, meaningful progress, waiting category, next observable event and checkpoint reference.
2. An interchangeable semantic-provider interface whose input is a sanitized evidence packet plus typed authority receipt only. The provider receives no repository roots, file handles, process runner, network client or mutation authority.
3. Exact-source bindings to the current UU-AAP frontier for Perceived Causal Liveness, Scoped Authority Evidence and Materialization Authority. These are compatibility/reference bindings, not claims that the canonical JavaScript evaluators are executed.

## Authority boundary

- Observe/Propose remain read-only.
- mutation budget remains 0.
- network model calls remain disabled.
- arbitrary process execution remains disabled.
- Execute is denied.
- no materialization authority is created.
- no ActionPermit is created.

## Terminal model

`COMPLETED | DENIED | INVALID | FAILED | CANCELLED`

A policy denial is a normal terminal outcome and is not a runtime failure.

## Source frontier

UU-AAP compatibility bindings are byte-bound to canonical main:

`f5673a39ddeef05f82c828f6cff554518f5f8ef6`

The semantic-provider boundary records whether the locally observed `uu-aap` HEAD matches this expected frontier.
