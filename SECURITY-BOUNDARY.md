# Matawaka Workbench — Security boundary

This document describes stable security/authority boundaries. Exact release identities belong to accepted tags, package previews and `PATCH-v*.md` history rather than permanent candidate-state prose.

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
Accepted Tag Discovery != Trust Discovery
Receipt Binding != Automatic Transition
Workbench Maintenance Authority != Catalog Mutation Authority != Agent Execute
```

No receipt from one stage silently authorizes a later stage.

## Update/build boundary

`Update candidate` sequences existing typed package intake, staging-only materialization, staged source plan and exact source apply/build services. Each retains its own fresh evidence checks and receipt. Apply/build rollback remains owned by the bounded apply/build service.

The orchestrator stops at:

`CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`

It does not call candidate launch, Self-test, checkpoint or publisher services.

## Separate launch boundary

**Запустить candidate** remains a separate exact-executable gate. It consumes the exact build receipt, rechecks the candidate/SemanticHost digests and launches only the receipt-bound local executable.

`Update candidate success != Launch authority`.

## Self-test boundary

Self-test is a read-only acceptance matrix with respect to update/build/launch/checkpoint/publication/lifecycle effects. Version-specific successors may add deterministic offline contract checks, but those checks must not scan or mutate real maintenance lifecycle artifacts as a hidden effect.

`Contract Check != Effect Exercise`.

## Local checkpoint boundary

Local acceptance requires a passing in-process Self-test receipt, exact acceptance artifact/running executable digest binding, exact accepted predecessor HEAD/tag and a build-source manifest matching the full changed source set.

The checkpoint gate may perform only its fixed local `git add/commit/tag` transaction after explicit confirmation. It creates no push/network/publication/lifecycle authority.

## Fixed accepted-source publication boundary

**Publish accepted** is a separate explicit human maintenance network gate with one destination only:

```text
remote = github-workbench
url = https://github.com/Matawaka/workbench.git
branch = refs/heads/main
tag = exact locally accepted workbench-v<version>-accepted
```

Remote main must be exact local parent or exact local HEAD. A conflicting main or accepted tag fails closed. Branch publication is non-force exact-head fast-forward only. Final remote main/tag readback must equal the local accepted HEAD and local HEAD/working tree must remain unchanged.

No arbitrary remote, force-push, tag movement, credential/protection mutation, catalog mutation, Agent Execute, ActionPermit or general Workbench network authority is created.

## Successor-generic Maintenance Lifecycle Receipt boundary

The lifecycle service is post-publication evidence composition only.

It may read:

- Workbench-local ignored artifact files;
- fixed read-only Git observations from a hard allowlist:
  - `rev-parse HEAD`;
  - `rev-parse HEAD^`;
  - `tag --points-at HEAD`;
  - `tag --points-at <exact-parent-sha>`;
  - `status --porcelain=v1 --untracked-files=all`.

It may not invoke Update candidate, source apply/build, candidate launch, Self-test, local checkpoint or publication services. It may not run Git push/fetch/remote/add/commit/tag mutation operations.

### Evidence routing

The service derives the current target only from exact accepted evidence:

1. require exactly one tag at current HEAD matching `workbench-v<version>-accepted`;
2. derive target version from that tag;
3. require one checkpoint with matching target version/tag and `NewHead == HEAD`;
4. require checkpoint `PreviousHead == HEAD^`;
5. require exactly one accepted predecessor tag at that parent commit;
6. load the checkpoint-bound acceptance artifact and require its exact SHA-256, version/schema and `Passed=true`;
7. require one orchestrator receipt whose target/predecessor and candidate executable digest match that acceptance/checkpoint evidence;
8. require one publication receipt whose version/tag/local head/parent and remote main/tag match the same accepted frontier;
9. bind SHA-256 of every consumed artifact;
10. require clean current Workbench source state.

No file may be selected by modification time. Missing or multiple qualifying tags/artifacts fail closed.

```text
Generic Evidence Discovery != Authority Discovery
Accepted Tag Discovery != Trust Discovery
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

## Qualification boundary

A real successor transition is required before calling the generic lifecycle adapter reusable. Offline contract checks or successful self-assessment of one release are insufficient.

Allowed qualification outcomes:

- `LIFECYCLE_REUSABLE`;
- `LIFECYCLE_NEEDS_ADAPTER`;
- `LIFECYCLE_AMBIGUOUS`;
- `LIFECYCLE_NOT_REQUIRED`.

A negative result is valid and blocks feature inflation.

## Prohibited effects

Qualification/stabilization work does not create or authorize:

- automatic update/build/launch/Self-test/checkpoint/publication/lifecycle execution;
- lifecycle retry or rollback;
- arbitrary Git/network operations;
- catalog mutation;
- Agent Execute or ActionPermit;
- trust/identity claims from tags or hashes;
- canonical UU-AAP conformance;
- Stable Core/interface-registry promotion.
