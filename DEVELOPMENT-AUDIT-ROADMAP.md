# Matawaka Workbench — Development Audit & Roadmap

## Current accepted frontier

Accepted and remotely published predecessor:

- commit `24c98787817b3b37f1a7197ecb5627be130f2581`;
- tag `workbench-v0.32-accepted`;
- parent `532c1c8220d160321c928055139aa8f76a0dc08b`;
- tree `93809ef0e9ec7a9471d7f9c4137c43d1aa529c32`.

v0.33 candidate source does not change that accepted frontier before local acceptance and separate publication.

## Established product/evidence layers

Historical development through v0.31 established bounded local analysis, authority-separated semantic/runtime execution, visible liveness, local acceptance, self-hosted update/build/launch, recovery/transport evidence and key-provenance/continuity/refusal boundaries.

v0.32 then converted those results into a cleaner product-maintenance surface:

- completed proof milestones removed from visible top-level controls but preserved in source/history;
- public docs brought closer to the product architecture;
- fixed `Matawaka/workbench` accepted-source publisher added;
- publisher proven and used to publish exact `workbench-v0.32-accepted`.

## v0.33 — Maintenance Update Orchestrator

Goal: reduce normal pre-launch update interaction without collapsing typed semantic/authority gates.

Visible target:

```text
Update candidate
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
```

Inside **Update candidate** the application sequences the existing:

```text
read-only package plan
-> fresh plan
-> staging-only materialization
-> fresh staged apply plan
-> exact source apply/build with existing rollback
```

Every sub-stage keeps its typed receipt and its own fresh evidence checks.

`One operator session != One semantic authority`

### v0.33 acceptance criteria

- Windows/.NET 10 Release build PASS;
- one visible Update candidate pre-launch entry point;
- old four pre-launch controls not visible;
- separate Launch remains visible and separately confirmed;
- orchestrator reuses existing typed services, not duplicate mutation/build logic;
- package/predecessor freshness fails closed;
- v0.33 Self-test is read-only and includes offline orchestrator/publisher successor checks;
- local target `workbench-v0.33-accepted` requires byte-bound build source manifest;
- publication remains fixed fast-forward-only `Matawaka/workbench` gate;
- source-only v0.33 package traverses accepted v0.32 GUI path;
- remote main/tag verified independently after publication.

## v0.34 — Maintenance Lifecycle Receipt

After v0.33 is accepted/published, add one non-authorizing audit summary linking:

`Update candidate receipt -> Launch receipt -> Self-test receipt -> Local checkpoint receipt -> Publication receipt`

The summary must expose missing/failed stages without creating an automatic transition or action lease.

```text
Lifecycle Summary != Authority
Update Completed != Candidate Accepted
Candidate Accepted != Source Published
Publication Failure != Retroactive Acceptance Failure
```

## Later research directions

Keep separate until independent evidence requires them:

- cross-machine/cross-OS transport portability;
- trusted producer identity/certificate/trust-anchor models;
- trusted time;
- real key revocation policy/enforcement authority;
- secure external distribution/signing;
- deeper Workbench ↔ UU-AAP reusable composition only where consumer demand is demonstrated.

## Architecture discipline

Workbench remains a product/application implementation informed by UU-AAP values and boundaries. Product utility does not automatically create a Core requirement.

```text
Product utility != Core requirement
Historical proof != Permanent UI obligation
UI consolidation != Evidence erasure
Maintenance automation != Authority collapse
Accepted local state != Published remote state
Publication capability != General network capability
```
