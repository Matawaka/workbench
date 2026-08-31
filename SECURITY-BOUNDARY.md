# Matawaka Workbench v0.34 candidate — Security boundary

Accepted predecessor: `workbench-v0.33-accepted` / `df211d1f4d80d0b1f238f1166460758e73ce18d2`.

The established semantic/runtime boundary remains unchanged: fixed verified SemanticHost, restricted Low-integrity token, Windows Job Object, runtime attestation before semantic input, read-only proposal behavior and denied Execute control.

## Maintenance distinctions

```text
One Update Candidate Confirmation != Authority Collapse
Successful Build != Candidate Launch
Candidate Launch != Self-test
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Remote Publication Authority
Publication Success != Lifecycle Authority
Lifecycle Summary != Lifecycle Authority
Observed Sequence != Authorized Sequence
Receipt Binding != Automatic Transition
Workbench Maintenance Authority != Catalog Mutation Authority != Agent Execute
```

## Accepted update and launch boundary

v0.34 keeps the accepted v0.33 `MaintenanceUpdateOrchestratorService` unchanged. It sequences the existing typed intake/materialize/staged-plan/apply-build gates, preserves their sub-receipts and stops before candidate launch.

**Запустить candidate** remains a separate exact-executable confirmation.

## v0.34 Self-test boundary

Self-test preserves the complete accepted v0.33 read-only matrix and adds only offline lifecycle contract checks. It validates categories such as:

- summary/action/authority fields remain false;
- fixed v0.34 target and exact v0.33 predecessor;
- missing lifecycle artifact is refused;
- ambiguous lifecycle artifact is refused.

Self-test does not scan actual lifecycle directories and performs no update/build/launch/checkpoint/publication/lifecycle-write effect.

## v0.34 local checkpoint boundary

`workbench-v0.34-accepted` requires:

- passing in-process v0.34 Self-test;
- exact acceptance artifact + running executable digest binding;
- current HEAD exactly `df211d1f...` and predecessor tag at that commit;
- exact dynamic v0.34 build-source manifest matching all changed files/bytes;
- explicit **Принять** confirmation.

Only fixed local add/commit/tag is admitted. Publication and lifecycle summary remain separate.

## v0.34 fixed publication boundary

**Publish accepted** retains one fixed destination and fast-forward-only semantics:

```text
remote = github-workbench
url = https://github.com/Matawaka/workbench.git
branch = refs/heads/main
tag = workbench-v0.34-accepted
```

Remote main must be exact predecessor or exact accepted HEAD. Conflicting main/tag fails closed. No force-push/tag movement/general network/catalog/Agent Execute authority is created.

## Maintenance Lifecycle Receipt boundary

**Lifecycle receipt** is post-publication evidence composition only.

The assessment may read:

- local Workbench artifact files;
- fixed read-only Git observations: `rev-parse HEAD`, `tag --points-at HEAD`, `status --porcelain`.

It may not invoke Update candidate, source apply/build, candidate launch, Self-test, local checkpoint or publication services.

A complete lifecycle relation requires exact bindings:

1. current accepted checkpoint at HEAD/tag;
2. checkpoint-bound Self-test artifact path and SHA-256;
3. passing v0.34 acceptance executable SHA-256;
4. unique v0.33 orchestrator receipt targeting v0.34 with the same built candidate executable SHA-256;
5. unique v0.34 publication receipt with local/remote main/tag equal the checkpoint accepted commit;
6. SHA-256 of every consumed artifact;
7. clean current Workbench source state.

Missing or multiple qualifying artifacts are not guessed or resolved by file age.

```text
Missing Artifact != Inferred Success
Ambiguous Artifact != Chosen Latest Artifact
Artifact Path != Artifact Identity
Same Executable Digest != Automatic Authority
Publication Success != Retroactive Update Authority
```

After a complete assessment, an explicit confirmation may write one local receipt under ignored `artifacts/lifecycle`. That write is evidence only and exposes:

- `AuthorityCreated=false`;
- `ActionPerformed=false`;
- `RetryAuthorized=false`;
- `RollbackAuthorized=false`.

## Prohibited effects

v0.34 lifecycle work does not create or authorize:

- package/source mutation, build or launch;
- Self-test/checkpoint/publication replay;
- Git push/fetch/remote mutation from the lifecycle service;
- retry or rollback;
- catalog mutation;
- Agent Execute or ActionPermit;
- general network authority;
- canonical UU-AAP conformance;
- Stable Core/interface-registry promotion.
