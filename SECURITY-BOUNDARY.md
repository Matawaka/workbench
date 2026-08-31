# Matawaka Workbench v0.33 candidate — Security boundary

Accepted predecessor: `workbench-v0.32-accepted` / `24c98787817b3b37f1a7197ecb5627be130f2581`.

The established semantic/runtime boundary remains unchanged: fixed verified SemanticHost, restricted Low-integrity token, Windows Job Object, runtime attestation before semantic input, read-only proposal behavior and denied Execute control.

## Maintenance distinctions

```text
Package Preview != Materialization Authority
One Update Candidate Confirmation != Authority Collapse
Plan Receipt != Materialization Receipt
Materialization Receipt != Source Apply Authority
READY Apply Plan != Source Mutation
Successful Build != Candidate Launch
Candidate Launch != Self-test
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Remote Publication Authority
Fixed Publication Authority != General Network Authority
Workbench Maintenance Authority != Catalog Mutation Authority != Agent Execute
```

## v0.33 Update candidate boundary

`MaintenanceUpdateOrchestratorService` sequences existing typed services only:

`LocalUpdateIntakeService -> LocalUpdateMaterializationService -> StagedUpdateApplyPlanService -> BoundedUpdateApplyBuildService`

The orchestrator itself does not implement a second source mutation/build path. It does not call candidate launch or fixed publication. It preserves every sub-receipt and stops at:

`CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`

Freshness requirements include:

- package bytes/digest unchanged after preview;
- fresh package plan equivalent to preview identity/digests;
- exact predecessor commit/tag still current;
- staging-only materialization receipt valid;
- fresh staged plan READY and non-authorizing;
- apply/build receipt exact target/predecessor and separate-launch;
- existing apply/build rollback remains responsible for restoration on failure.

A stale or consumed session cannot silently authorize a later mutation.

## Separate Launch boundary

**Запустить candidate** remains the existing separate explicit gate. It consumes the exact successful `WorkbenchUpdateApplyBuildReceipt`, rechecks candidate/SemanticHost digests and predecessor source state, and launches only the exact receipt-bound executable.

`Update candidate success != Launch authority`

## v0.33 Self-test boundary

Self-test preserves the complete accepted v0.32 read-only semantic/runtime and publisher-contract matrix and adds only deterministic offline checks for:

- orchestrator non-authorizing preview;
- typed service reuse;
- stop-before-launch behavior;
- v0.33 fixed publisher remote/tag/conflict classifications.

Self-test performs no package update effect, build, launch, checkpoint, remote read or publication.

## v0.33 local checkpoint boundary

A local `workbench-v0.33-accepted` checkpoint requires:

- passing in-process v0.33 Self-test receipt;
- exact acceptance artifact/executable digest match;
- HEAD at exact `workbench-v0.32-accepted` predecessor;
- exact v0.33 build-source manifest matching all changed source bytes;
- explicit **Принять** confirmation.

This gate may only perform fixed local `git add/commit/tag`. It creates no push/network/publication authority.

## v0.33 fixed GitHub publication boundary

After local acceptance, **Publish accepted** uses only:

```text
remote = github-workbench
url = https://github.com/Matawaka/workbench.git
branch = refs/heads/main
tag = workbench-v0.33-accepted
```

Remote main must be exact local parent or exact local HEAD. A conflicting main or tag fails closed. Main update is non-force exact-head fast-forward only. The accepted tag may only be absent or already exact HEAD. Final remote main/tag readback must equal local accepted HEAD, and local HEAD/working tree must remain unchanged.

No arbitrary remote, force-push, tag movement, catalog mutation, Agent Execute, ActionPermit, credentials/protection mutation, general network authority, canonical UU-AAP conformance or Stable Core/interface-registry promotion is authorized.

## Historical evidence

Historical recovery/transport/key and pre-v0.33 update controls may remain as collapsed compatibility bindings for accepted legacy liveness code. They are not visible/focusable/clickable product controls.

`UI consolidation != Historical evidence deletion`
