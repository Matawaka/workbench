# Matawaka Workbench — Development Audit & Roadmap

## Current accepted frontier

As of v0.32 candidate development, the remotely accepted Workbench remains:

- commit `532c1c8220d160321c928055139aa8f76a0dc08b`;
- tag `workbench-v0.31-accepted`;
- tree `ca438434c2c2b093f6aadb5c306a05ad03493870`.

Candidate source does not change that accepted frontier.

## What the evidence-development line already established

The historical sequence through v0.31 produced reusable implementation/evidence around:

- bounded local Matawaka analysis;
- read-only agent authority separation;
- semantic provider interchangeability;
- restricted Windows SemanticHost process boundary and runtime attestation;
- visible progress/liveness without hidden reasoning disclosure;
- local Self-test and accepted checkpoint;
- self-hosted exact candidate update/apply/build/launch;
- recovery admission, negative controls, closure, replay and relocation;
- transport independence and adversarial transport controls;
- producer-key provenance, fixture rotation continuity and revocation-inference refusal;
- preservation of historical evidence when future key policy remains unresolved.

These remain source/history evidence. They are not all active day-to-day operator actions.

## Product audit finding at v0.31

Two kinds of surface had become mixed:

1. current reusable product capabilities;
2. one-off/completed evidence-development milestones.

This created visual and maintenance entropy: 30+ toolbar click handlers, with many controls representing completed proof stages rather than normal product work.

A second recurring burden was accepted-source publication: every accepted version needed an external generated script even though the target repository and permitted Git transition were always the same.

A third problem was documentation drift: public README/start/security pages still described v0.14 while implementation had reached v0.31.

## v0.32 decision — Maintenance Integrator

v0.32 is therefore an operational simplification increment, not a new UU-AAP primitive.

### Active responsibilities

The application should optimize for five stable responsibilities:

1. bounded local Matawaka analysis and inspection;
2. visible evidence / authority / liveness state;
3. safe candidate update, build, launch and local acceptance;
4. explicit fixed accepted-source publication;
5. maintenance recovery.

Historical proof code remains available for audit and regression, but no longer requires permanent top-level buttons.

### Publication boundary

The accepted-source publisher is deliberately narrow:

`local accepted checkpoint -> explicit Publish accepted -> fixed Matawaka/workbench fast-forward/tag publication -> exact readback receipt`

It does not generalize into arbitrary Git remote management or agent network execution.

## v0.33 — Maintenance Update Orchestrator

Goal: reduce the normal five-button update sequence to one **Update candidate** operator session while preserving all internal boundaries.

Desired visible sequence:

```text
select package
-> inspect aggregate preview
-> explicit start
-> plan sub-receipt
-> materialization sub-receipt
-> apply-plan sub-receipt
-> apply/build sub-receipt
-> launch candidate decision
```

Constraints:

- no hidden authority carry-forward between sub-stages;
- every predecessor receipt remains typed and individually inspectable;
- stale input at any stage fails closed;
- rollback behavior remains explicit;
- candidate launch remains separately observable and may still require its own confirmation;
- no automatic Self-test/Accept/Publish after candidate launch.

`One operator session != One semantic authority`

## v0.34 — Maintenance Lifecycle Receipt

Goal: provide one audit summary linking:

`Update candidate -> Self-test -> Local checkpoint -> Publish accepted`

The lifecycle receipt should bind the exact receipts/digests from each stage and expose completion/gaps without creating a new action gate.

Constraints:

- summary != authority;
- presence of update receipt != acceptance;
- acceptance != publication;
- publication failure does not retroactively invalidate a valid local acceptance;
- retry/successor events append rather than rewrite earlier evidence.

## Later research directions

These should remain separate from operational UI simplification until independent evidence demands them:

- cross-machine/cross-OS transport portability;
- trusted producer identity and certificate/trust-anchor models;
- trusted time;
- real key revocation policy and enforcement authority;
- external secure distribution/signing;
- deeper reusable composition with UU-AAP components where independent product demand is demonstrated.

## Architecture discipline

Workbench remains a product/application implementation that consumes and mirrors UU-AAP values and boundaries. Its successful operation does not itself promote product-specific machinery into Stable Core.

```text
Product utility != Core requirement
Historical proof != Permanent UI obligation
UI consolidation != Evidence erasure
Maintenance automation != Authority collapse
Accepted local state != Published remote state
Publication capability != General network capability
```
