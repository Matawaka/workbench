# Matawaka Workbench v0.32 candidate — Security boundary

The established semantic/runtime boundary remains unchanged: fixed verified SemanticHost, restricted Low-integrity token, Windows Job Object, runtime attestation before semantic input, read-only proposal behavior and denied Execute control. v0.32 does not widen semantic-provider authority.

## Core maintenance distinctions

```text
Self-test PASS != Checkpoint authority
Valid package != Materialization authority
Staging materialization != Source apply/build authority
READY source plan != Source mutation authority
Apply/build authority != Candidate launch authority
Candidate launch != Acceptance
Accepted checkpoint != Remote publication authority
Fixed publication authority != General network authority
Workbench maintenance authority != Catalog mutation authority != Agent Execute
```

No receipt from an earlier gate is treated as authorization for a later gate. Every effectful step must revalidate its own current evidence and receive its own explicit operator confirmation.

## Candidate update boundary

The existing self-hosted update chain remains intentionally decomposed:

`package intake -> staging materialization -> exact source delta plan -> exact source apply/build -> exact candidate launch -> read-only Self-test -> local accepted checkpoint`

Source apply is limited to exact planned payload bytes. Build/publish processes are limited to the fixed workspace-local `.dotnet-sdk\dotnet.exe` with fixed `build/publish --no-restore` arguments. Candidate launch is limited to the exact receipt-bound executable digest.

`--no-restore` means the build gate requests no package restore. It is not evidence of OS-level network isolation.

## v0.32 Self-test boundary

The v0.32 Self-test reuses the complete accepted v0.31 read-only semantic/runtime matrix and adds deterministic offline checks for the publisher contract only.

Self-test MUST NOT:

- call `git ls-remote`;
- add/change a Git remote;
- push a branch or tag;
- exercise the `Publish accepted` network effect;
- infer network authority from existence of the publisher code;
- create Agent Execute, ActionPermit or catalog mutation authority.

Therefore:

`Publisher Contract Check != Publication Effect`

## Fixed GitHub publication authority

The new publication service is a separate explicit human maintenance network gate.

Its fixed identity is:

```text
remote = github-workbench
url = https://github.com/Matawaka/workbench.git
branch = refs/heads/main
tag = workbench-v0.32-accepted
```

Before any network effect, local preview requires:

- clean Workbench working tree;
- current HEAD;
- exact HEAD parent;
- local `workbench-v0.32-accepted` pointing exactly at HEAD;
- fixed remote either absent or already mapped to the exact fixed URL.

After explicit **Publish accepted** confirmation, the service may:

1. add the fixed remote only when absent;
2. read exact remote `main`/tag refs;
3. push exact accepted HEAD to remote `main` only when remote main equals exact local parent;
4. do nothing to main when it already equals exact local HEAD;
5. publish the accepted tag only when absent;
6. do nothing to the tag when it already resolves to exact local HEAD;
7. read both refs back and require exact equality;
8. verify local HEAD and working tree did not change;
9. write one local publication receipt.

It fails closed when remote main is any third state or the accepted tag conflicts.

## Explicit prohibitions

The publication path has no authority to:

- use a caller-supplied remote name or URL;
- use `--force`, `--force-with-lease`, delete/refspec rewrite or history rewrite;
- move or replace an existing conflicting tag;
- create an unrelated remote main history;
- publish any branch/tag other than the fixed main/tag pair;
- mutate Matawaka catalog repositories;
- invoke Agent Execute or create ActionPermit;
- provide general network access to Workbench runtime/semantic providers;
- change credentials/secrets/protection settings;
- claim canonical UU-AAP conformance, Stable Core membership, identity, trust, trusted time, legal effect or release authority outside this fixed source-publication scope.

```text
Fast-forward permission != Force-push permission
Remote main update != Tag movement authority
Fixed repository network authority != General network authority
Source publication != Catalog mutation
Source publication != Canonical UU-AAP conformance
```

## Partial-success recovery

Publication is intentionally retry-safe for one bounded partial-success state:

`remote main == exact accepted HEAD && accepted tag absent`

A retry may publish only the missing exact tag after revalidating local accepted state and remote main. This recovery path does not authorize any broader remote mutation.

## Historical evidence

v0.32 removes completed recovery/transport/key milestone controls from the active toolbar only. Their service source, receipts, patch notes and Git history remain intact.

`Historical evidence UI removal != Historical evidence deletion`
