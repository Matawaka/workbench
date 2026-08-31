# Matawaka Workbench v0.33 candidate

Windows/.NET 10 WPF control plane for bounded local Matawaka analysis, evidence/authority inspection, self-hosted maintenance, recovery and explicit accepted-source publication.

## Accepted predecessor

The currently accepted and remotely published Workbench is:

- commit `24c98787817b3b37f1a7197ecb5627be130f2581`;
- tag `workbench-v0.32-accepted`;
- parent `532c1c8220d160321c928055139aa8f76a0dc08b`.

v0.33 candidate source does not change that accepted frontier until it independently traverses update → launch → Self-test → local checkpoint → Publish accepted.

## Architecture

Workbench is a product/application layer, not UU-AAP Stable Core:

- `Matawaka.Workbench.App` — WPF operator surface and maintenance gates;
- `Matawaka.Workbench.Runtime` — command routing/runtime composition;
- `Matawaka.Workbench.Protocol` — Workbench-local typed contracts/progress semantics;
- `Matawaka.Workbench.AgentHost` — bounded development-agent host and Windows process/security boundary;
- `Matawaka.Workbench.Engine` — reusable analytic future adapter;
- `Matawaka.Workbench.Catalog` — local Matawaka catalog inspection;
- `Matawaka.Workbench.SemanticHost` — fixed verified semantic host.

The established semantic/runtime line remains unchanged: restricted Low-integrity token, Windows Job Object, runtime attestation before semantic input, byte-bound SemanticHost, read-only proposal path and denied Execute acceptance control.

## v0.33 product change — Maintenance Update Orchestrator

v0.32 proved the fixed accepted-source publisher and consolidated historical proof controls out of the active toolbar. v0.33 addresses the remaining normal-update operator burden.

Previous visible pre-launch sequence:

`Пакет обновления → Материализовать → План применения → Применить + собрать → Запустить candidate`

v0.33 visible sequence:

`Update candidate → Запустить candidate`

The single **Update candidate** session does not collapse the underlying typed boundaries. `MaintenanceUpdateOrchestratorService` sequences the already-existing:

`LocalUpdateIntakeService → LocalUpdateMaterializationService → StagedUpdateApplyPlanService → BoundedUpdateApplyBuildService`

Each service still revalidates its own current evidence and emits its own receipt. The aggregate orchestrator receipt binds those sub-receipts but does not replace them.

```text
One operator session != One semantic authority
Package Preview != Materialization Authority
Plan Receipt != Materialization Receipt
Materialization Receipt != Source Apply Authority
READY Apply Plan != Source Mutation
Successful Build != Candidate Launch
```

**Запустить candidate** remains a separate explicit exact-executable decision and uses the already accepted launch gate.

## v0.33 acceptance/publication chain

After a launched v0.33 candidate:

1. enable **Агент включен**;
2. run **Self-test** — complete v0.32 semantic/runtime/publisher matrix + offline v0.33 orchestrator and publisher-successor contract checks;
3. require `Passed=true`;
4. **Принять** — local `workbench-v0.33-accepted` only, byte-bound to the built source manifest;
5. **Publish accepted** — separate fixed GitHub network decision.

The v0.33 fixed publisher retains the accepted v0.32 constraints:

- `github-workbench` only;
- `https://github.com/Matawaka/workbench.git` only;
- remote main must be exact parent or exact local HEAD;
- non-force fast-forward only;
- accepted tag must be absent or exact HEAD;
- conflicting main/tag fails closed;
- exact remote main/tag readback required;
- local HEAD/working tree unchanged.

`Accepted checkpoint != Remote publication authority`

## Non-effects

v0.33 does not create:

- automatic candidate launch;
- automatic Self-test/Accept/Publish;
- Agent Execute or ActionPermit;
- catalog mutation authority;
- general network authority;
- arbitrary Git remote/history rewrite authority;
- canonical UU-AAP conformance or Stable Core/interface-registry promotion.

Historical recovery/transport/key evidence remains in source and Git history even when not visible as permanent toolbar controls.

## Next planned layer

v0.34 may add a **Maintenance Lifecycle Receipt** that summarizes `Update candidate → Self-test → local checkpoint → publication` without authorizing or automating any of those effects.
