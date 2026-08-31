# Matawaka Workbench v0.32 — Maintenance Integrator & Surface Consolidation

Status: candidate source layer over exact accepted `workbench-v0.31-accepted`. No remote acceptance/publication has occurred merely because this source exists.

## Exact predecessor

- accepted predecessor commit: `532c1c8220d160321c928055139aa8f76a0dc08b`
- accepted predecessor tag: `workbench-v0.31-accepted`
- predecessor tree: `ca438434c2c2b093f6aadb5c306a05ad03493870`

## Purpose

v0.31 completed the current recovery/transport/key evidence-development line but left two product-maintenance problems:

1. the main toolbar accumulated one permanent button for each historical proof milestone;
2. every accepted version still needed an externally generated publication script to fast-forward the same dedicated GitHub repository.

v0.32 consolidates the active operator surface and adds one narrow, auditable in-app source-publication gate.

## Active-surface consolidation

The visible toolbar keeps current product responsibilities:

- JSON paste/file/validate/run;
- Self-test / Accept;
- package → materialize → apply plan → apply+build → launch candidate;
- Recovery check / plan / execute;
- Publish accepted;
- Stop;
- Agent enabled and catalog fetch controls.

The following completed evidence-development controls are removed from the active toolbar only:

- Recovery admission;
- Recovery negatives;
- Recovery closure;
- Recovery replay;
- Recovery relocate;
- old Recovery export/import;
- Transport independence;
- Transport negatives;
- Transport closure;
- Key provenance;
- Key continuity;
- Revocation boundary.

Their service code, patch notes, receipts and Git history are retained.

`Historical evidence UI removal != Historical evidence erasure`

## v0.32 acceptance successor

The historical v0.31 acceptance/checkpoint implementations remain unchanged. v0.32 adds successor wrappers:

- `WorkbenchV032AcceptanceHarness` — runs the complete v0.31 read-only matrix plus offline publisher-contract checks; no publication network effect occurs in Self-test;
- `LocalCheckpointV032Service` — accepts only a passing v0.32 receipt, exact v0.31 accepted predecessor and byte-bound v0.32 build-source manifest; produces local `workbench-v0.32-accepted` only after explicit confirmation.

Thus:

`Self-test PASS != Local checkpoint authority != Remote publication authority`

## Fixed GitHub publication service

`FixedGitHubPublicationService` is a separate human-confirmed maintenance network boundary.

Fixed target:

```text
remote = github-workbench
url = https://github.com/Matawaka/workbench.git
branch = refs/heads/main
tag = workbench-v0.32-accepted
```

### Local preview — no network effect

Before confirmation the service requires:

- clean Workbench working tree;
- current HEAD and exact parent derived from Git;
- local `workbench-v0.32-accepted` resolving exactly to HEAD;
- fixed remote either absent or already mapped to the exact fixed URL.

No `ls-remote`, push or remote creation occurs before the operator sees this preview.

### Confirmed effect

After explicit **Publish accepted** confirmation:

- fixed remote may be added only if absent;
- remote main and tag are read;
- remote main must equal exact local parent or exact local HEAD;
- conflicting remote main fails closed;
- conflicting accepted tag fails closed;
- exact accepted HEAD may fast-forward remote main only from exact parent;
- exact accepted tag may be published only if absent;
- no force push is admitted;
- remote main/tag are read back and must both equal exact accepted local HEAD;
- local HEAD and working tree must remain unchanged;
- a bounded publication receipt is written under ignored `artifacts/publication`.

### Partial-success recovery

If exact remote main already equals accepted local HEAD but the accepted tag is absent, a retry may publish only that missing exact tag. This makes the intended partial-success recovery idempotent without widening remote authority.

## Authority invariants

```text
Accepted checkpoint != Remote publication authority
Publish button != Agent Execute
Fixed repository network authority != General network authority
Fast-forward permission != Force-push permission
Remote main update != Tag movement authority
Source publication != Catalog mutation
Source publication != Canonical UU-AAP conformance
Historical evidence UI removal != Historical evidence erasure
```

The publisher does not authorize credentials/protection changes, arbitrary remotes, history rewrite, catalog mutation, Agent Execute, ActionPermit, canonical UU-AAP conformance or Stable Core/interface-registry promotion.

## Candidate acceptance sequence

1. Build a source-only v0.32 package from exact accepted v0.31.
2. In accepted v0.31: **Пакет обновления → Материализовать → План применения → Применить + собрать → Запустить candidate**.
3. In launched candidate: enable **Агент включен**, run **Self-test**, require PASS.
4. **Принять** → create exact local `workbench-v0.32-accepted`.
5. **Publish accepted** → separately confirm fixed GitHub network effect.
6. Require remote main/tag == exact local accepted HEAD and local repository unchanged.

Only after step 6 is v0.32 both locally accepted and remotely published.

## Next increments

- **v0.33 — Maintenance Update Orchestrator:** one `Update candidate` operator session over existing typed plan/materialize/apply/build/launch sub-receipts. UI simplification must not collapse semantic/authority gates.
- **v0.34 — Maintenance Lifecycle Receipt:** summarize update → Self-test → checkpoint → publication as one audit view without making any later effect automatic.

Cross-machine portability, trusted producer identity, trust anchors, trusted time and real revocation policy remain later research directions.
