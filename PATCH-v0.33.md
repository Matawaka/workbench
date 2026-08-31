# Matawaka Workbench v0.33 — Maintenance Update Orchestrator

Status: candidate source layer over exact accepted/published `workbench-v0.32-accepted`. No v0.33 acceptance/publication occurs merely because this source exists.

## Exact predecessor

- commit `24c98787817b3b37f1a7197ecb5627be130f2581`
- tag `workbench-v0.32-accepted`
- tree `93809ef0e9ec7a9471d7f9c4137c43d1aa529c32`
- parent `532c1c8220d160321c928055139aa8f76a0dc08b`

## Purpose

v0.32 reduced historical proof controls and introduced the fixed in-app accepted-source publisher. The remaining routine maintenance burden was the four-button pre-launch update sequence:

`Пакет обновления -> Материализовать -> План применения -> Применить + собрать`

v0.33 replaces those four visible controls with one **Update candidate** maintenance session while preserving the existing typed services, receipts, freshness checks and fail-closed rollback.

## Orchestrator

`MaintenanceUpdateOrchestratorService` composes only existing accepted gates:

`LocalUpdateIntakeService -> LocalUpdateMaterializationService -> StagedUpdateApplyPlanService -> BoundedUpdateApplyBuildService`

### Prepare

Before any update effect, `PrepareAsync`:

- verifies the local package through existing intake;
- requires exact accepted predecessor/READY plan;
- binds package SHA-256, target and predecessor;
- produces a non-authorizing preview.

`EffectAuthorized=false` is mandatory.

### Execute confirmed session

After one explicit **Update candidate** confirmation:

1. package SHA is checked again;
2. intake is rerun and fresh plan must equal the preview identity/payload;
3. existing materializer revalidates package/predecessor and writes staging-only bytes/receipt;
4. existing staged planner revalidates predecessor/staging bytes and emits non-authorizing READY plan;
5. existing apply/build service performs its own fresh plan and exact source transaction/build with existing rollback behavior;
6. orchestrator emits an aggregate receipt binding all typed sub-receipts/artifacts;
7. execution stops before candidate launch.

Passing status:

`CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`

## Separate launch preserved

The visible **Запустить candidate** button remains separate. It consumes only the exact existing apply/build receipt and retains the accepted executable/SemanticHost digest confirmation path.

`Successful Build != Candidate Launch`

## v0.33 acceptance successor

`WorkbenchV033AcceptanceHarness` preserves the complete accepted v0.32 read-only semantic/runtime and fixed-publisher contract matrix and adds only offline checks for:

- orchestrator preview non-authority;
- typed service reuse;
- stop-before-launch behavior;
- v0.33 fixed publisher remote/tag/conflict semantics.

Self-test performs no update/build/launch/checkpoint/publication effect.

`LocalCheckpointV033Service` requires a passing v0.33 acceptance receipt, exact v0.32 accepted predecessor and byte-bound `matawaka.workbench-build-source-manifest/v0.33` before explicit local `workbench-v0.33-accepted` creation.

Checkpoint has no push/network/publication authority.

## v0.33 publication successor

`FixedGitHubPublicationV033Service` preserves the accepted v0.32 fixed publication boundary with target tag changed to `workbench-v0.33-accepted`:

```text
remote = github-workbench
url = https://github.com/Matawaka/workbench.git
branch = refs/heads/main
tag = workbench-v0.33-accepted
```

Remote main must be exact local parent or exact local HEAD. Conflicting main/tag fails closed. Only non-force exact-head fast-forward and missing exact-tag publication are admitted. Exact remote readback and unchanged local HEAD/working tree are required.

## Authority invariants

```text
Package Preview != Materialization Authority
One Update Candidate Confirmation != Authority Collapse
Plan Receipt != Materialization Receipt
Materialization Receipt != Source Apply Authority
READY Apply Plan != Source Mutation
Successful Build != Candidate Launch
Candidate Launch != Self-test
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Publish Authority
Orchestration Receipt != ActionPermit
Fixed Publication Authority != General Network Authority
```

No automatic Self-test/Accept/Publish, Agent Execute, ActionPermit, catalog mutation, general network authority, arbitrary remote/history rewrite, canonical UU-AAP conformance or Stable Core/interface-registry promotion is created.

## Acceptance sequence

1. Produce source-only v0.33 package from exact accepted v0.32.
2. In accepted v0.32 click **Update candidate**, inspect preview, explicitly confirm.
3. Require aggregate build receipt `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
4. Separately **Запустить candidate** and confirm exact executable digest.
5. In launched v0.33 run Self-test and require PASS.
6. Separately **Принять** -> local `workbench-v0.33-accepted`.
7. Separately **Publish accepted**.
8. Independently require remote main/tag == exact accepted HEAD and local state unchanged.

## Next

After v0.33 acceptance/publication, v0.34 may add a non-authorizing Maintenance Lifecycle Receipt linking update, launch, Self-test, checkpoint and publication receipts without making any transition automatic.
