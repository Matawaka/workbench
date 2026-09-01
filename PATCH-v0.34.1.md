# Matawaka Workbench v0.34.1 — Qualification & Stabilization

Status: patch-level candidate over exact accepted/published v0.34. This is not a v0.35 feature layer.

## Exact predecessor

- commit: `224ad00bd72b0534de1081b2a20c44746ee0e7a0`
- tag: `workbench-v0.34-accepted`
- tree: `7b2c47441115591860b8ee466073d391796a4a78`
- parent: `df211d1f4d80d0b1f238f1166460758e73ce18d2`

## Why this patch exists

The first complete Maintenance Lifecycle Receipt proved one exact v0.34 lifecycle, but qualification exposed two recurring stabilization defects.

### 1. Lifecycle was self-bound, not successor-reusable

The accepted v0.34 lifecycle service hard-coded its own version, tag, predecessor and artifact patterns. Result before patch:

`LIFECYCLE_NEEDS_ADAPTER`

It could prove v0.34 but could not itself assess the next successor transition required by the roadmap.

### 2. Permanent docs remained candidate-state

After v0.34 was accepted/published, README/START/SECURITY/ROADMAP still called it a candidate and named v0.33 as the accepted baseline.

`STABILIZATION_REQUIRED`

This is repaired at the documentation model level rather than by another one-off accepted-SHA edit.

## Generic lifecycle evidence adapter

`MaintenanceLifecycleReceiptService` no longer owns release-specific target/predecessor constants.

It derives one current accepted relation from exact evidence:

```text
HEAD
+ unique workbench-v<version>-accepted at HEAD
+ exact HEAD parent
+ unique accepted tag at parent
+ unique checkpoint for HEAD/tag/version
+ checkpoint-bound passing Self-test artifact + SHA-256
+ unique matching orchestrator target/predecessor/executable
+ unique matching fixed publication local/remote refs
+ exact artifact hashes + clean source
-> lifecycle assessment
```

### Fail-closed rules

- 0 accepted tags at HEAD → refuse;
- >1 accepted tags at HEAD → refuse;
- 0/>1 accepted predecessor tags → refuse;
- 0/>1 matching checkpoint/orchestrator/publication artifacts → refuse;
- checkpoint acceptance digest drift → refuse;
- executable digest discontinuity → refuse;
- Git parent/checkpoint predecessor mismatch → refuse;
- dirty source → refuse.

Artifact modification time is never a selection rule.

The lifecycle Git helper has a fixed read-only allowlist only:

- `rev-parse HEAD`;
- `rev-parse HEAD^`;
- `tag --points-at HEAD`;
- `tag --points-at <exact-parent-sha>`;
- `status --porcelain=v1 --untracked-files=all`.

No push/fetch/remote/add/commit/tag mutation is available through the lifecycle service.

```text
Generic Evidence Discovery != Authority Discovery
Accepted Tag Discovery != Trust Discovery
Summary != Authority
```

## Public documentation stabilization

Permanent `README.md`, `START-HERE.md`, `SECURITY-BOUNDARY.md` and `DEVELOPMENT-AUDIT-ROADMAP.md` are lifecycle-state-neutral.

They no longer embed:

- `candidate` as the default-branch status;
- a previous release as the current accepted baseline.

Exact release state belongs to:

- accepted tag at `main` HEAD;
- `PATCH-v*.md`;
- package preview;
- issue/Git history.

`Accepted Source Documentation != Candidate Planning Document`

## Patch acceptance successor

v0.34.1 adds only the version boundary needed to run a real successor qualification:

- Self-test schema/version `v0.34.1 / 0.34.1`;
- predecessor exact accepted v0.34;
- local target `workbench-v0.34.1-accepted`;
- fixed publisher target `workbench-v0.34.1-accepted` with the same non-force fast-forward contract;
- generic Lifecycle receipt remains a separate post-publication action.

The build-source manifest intentionally keeps major.minor schema identity `.../v0.34` while preserving full `Version=0.34.1` and `v0.34.1-source-manifest...` filename, matching the accepted `BoundedUpdateApplyBuildService` policy.

## Qualification sequence

Use the accepted v0.34 application:

1. **Update candidate** with the v0.34.1 source-only package.
2. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
3. Separately **Запустить candidate**.
4. In v0.34.1 enable Agent and run **Self-test**; require PASS.
5. Separately **Принять** → local `workbench-v0.34.1-accepted`.
6. Separately **Publish accepted**.
7. Separately run the same generic **Lifecycle receipt**.
8. Independently verify remote main/tag and accepted source bytes.

Only then classify:

- generic `Complete=true` without manual artifact reconciliation → `LIFECYCLE_REUSABLE`;
- missing relation → `LIFECYCLE_NEEDS_ADAPTER`;
- ambiguous evidence → `LIFECYCLE_AMBIGUOUS`;
- aggregate adds no useful evidence → `LIFECYCLE_NOT_REQUIRED`.

Until step 8 completes, machine qualification status remains:

`REAL_SUCCESSOR_QUALIFICATION_PENDING`

## Non-effects

v0.34.1 does not create:

- a new feature layer;
- automatic update/launch/Self-test/Accept/Publish/Lifecycle execution;
- lifecycle retry/rollback authority;
- Agent Execute or ActionPermit expansion;
- general network or catalog authority;
- trust/identity claims from accepted tags;
- Runtime/Protocol/AgentHost/Engine/Catalog/SemanticHost changes;
- canonical UU-AAP conformance;
- Stable Core/interface-registry promotion.
