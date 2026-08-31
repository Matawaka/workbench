# Matawaka Workbench v0.34 — Maintenance Lifecycle Receipt

Status: candidate source layer over exact accepted/published `workbench-v0.33-accepted`. No v0.34 acceptance/publication/lifecycle success is claimed merely because this source exists.

## Exact predecessor

- commit: `df211d1f4d80d0b1f238f1166460758e73ce18d2`
- tag: `workbench-v0.33-accepted`
- tree: `ea1fde5f211534f1293e3bb53a594ba612b647ed`

## Purpose

v0.33 reduced the normal pre-launch update path to one **Update candidate** maintenance session while preserving separate launch, Self-test, checkpoint and publication decisions.

v0.34 adds a post-publication evidence view answering a different question:

> Can the already-completed independent maintenance receipts be proven to describe one exact successor transition without guessing by timestamp or filename order?

The answer is materialized only as a non-authorizing lifecycle receipt.

## Lifecycle relation

A complete assessment binds:

1. current local v0.34 checkpoint at HEAD / `workbench-v0.34-accepted`;
2. the exact Self-test artifact path carried by that checkpoint;
3. exact SHA-256 of that acceptance artifact;
4. `Passed=true`, v0.34 schema/version and accepted executable SHA-256;
5. the unique v0.33 orchestrator receipt targeting v0.34 whose ApplyBuild candidate executable SHA-256 equals the accepted executable;
6. the unique v0.34 publication receipt whose local head equals checkpoint new head and whose remote main/tag after publication equal that same commit;
7. SHA-256 of orchestrator, acceptance, checkpoint and publication artifact files;
8. current local HEAD/tag and clean source state.

`Complete=true` is refused if any relation is absent, changed or ambiguous.

## Ambiguity policy

The lifecycle service does not select a convenient “latest” match.

```text
0 qualifying artifacts -> fail closed
1 qualifying artifact  -> bind exact path + SHA-256
>1 qualifying artifacts -> fail closed as ambiguous
```

This is intentionally stricter than chronological inference.

## UI

A new **Lifecycle receipt** button is visible beside the existing explicit maintenance controls.

It runs after publication only:

1. read-only lifecycle assessment;
2. operator sees exact accepted commit, executable digest and four artifact digests;
3. explicit confirmation;
4. one local evidence receipt under ignored `artifacts/lifecycle`.

It does not call any maintenance effect service.

## Self-test

v0.34 Self-test preserves the full accepted v0.33 matrix and adds only offline lifecycle contract checks, including missing/ambiguous-artifact refusal helpers.

Self-test does not scan real lifecycle artifacts or write a lifecycle receipt.

## Checkpoint and publication successors

- local acceptance target: `workbench-v0.34-accepted`;
- exact predecessor commit is hard-bound to `df211d1f...` in the checkpoint gate;
- dynamic v0.34 build-source manifest must match every changed source byte;
- publication remains fixed `github-workbench` / `https://github.com/Matawaka/workbench.git`;
- remote main may only fast-forward from exact v0.33 parent or already equal exact accepted HEAD;
- no force push or conflicting tag movement;
- publication does not automatically create lifecycle evidence.

## Invariants

```text
Summary != Authority
Observed Sequence != Authorized Sequence
Receipt Binding != Automatic Transition
Missing Artifact != Inferred Success
Ambiguous Artifact != Chosen Latest Artifact
Artifact Path != Artifact Identity
Publication Success != Retroactive Update Authority
Lifecycle Receipt != ActionPermit
```

Lifecycle receipt fields explicitly preserve:

- `AuthorityCreated=false`;
- `ActionPerformed=false`;
- `RetryAuthorized=false`;
- `RollbackAuthorized=false`.

## Non-effects

No new Agent Execute, ActionPermit, catalog mutation, general network authority, arbitrary Git remote/history rewrite, automatic update/launch/Self-test/checkpoint/publication, retry/rollback, canonical UU-AAP conformance or Stable Core/interface-registry promotion.

## Acceptance sequence

1. accepted v0.33 **Update candidate** selects the v0.34 source-only ZIP;
2. require successful typed orchestration/build;
3. separately **Запустить candidate**;
4. v0.34 **Self-test** PASS;
5. explicitly **Принять** `workbench-v0.34-accepted`;
6. separately **Publish accepted**;
7. separately **Lifecycle receipt** and require complete exact binding;
8. independently verify remote main/tag and accepted bytes.

## After v0.34

Do not automatically add a v0.35 feature. First qualify lifecycle reuse on a real successor transition and admit negative outcomes such as ambiguity, adapter need or aggregate-not-required.
