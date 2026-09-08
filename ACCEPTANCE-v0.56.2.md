# Workbench v0.56.2 — bounded acceptance closure

Status: `CANDIDATE_NOT_ACCEPTED_NOT_PUBLISHED`

This successor does not reinterpret the already merged v0.56.1 provenance evidence. It adds a fresh acceptance identity because adding acceptance/publication machinery changes the source tree after the exact human-tested v0.56.1 frontier.

## Qualified predecessor

The fixed v0.56.1 source/evidence anchor is:

- canonical main `991fc9c08431141a63a8b53c2f0f138a4f58dc28`;
- tree `8640750016e5c6512231a237cf9456899cca1db1`;
- post-merge workflow_dispatch run `34020015287` = SUCCESS;
- post-merge artifact `9985185887`;
- artifact SHA-256 `5bd2cf7902321ae8b55dddd9675bebf734dc0261f86587fb1c05ac75906e1975`.

Human-accepted Russian wording remains:

`СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ`

That wording is evidence presentation only. It does not create trust, truth, authenticity, permission, publication authority, runtime/model authority, response/display authority, ActionPermit or SuccessorPermit.

## Accepted predecessor continuity

The only accepted/public predecessor remains `workbench-v0.55.2-accepted`, annotated tag object `81fbd3d6265b497e7509b7655554789b5a0dcf8d`, peeling to `ea852feeb0e8d92a8977bb251693e7e977913dca`.

The accepted predecessor tag is observed unsigned. This fact is preserved and is not promoted into cryptographic trust.

## Two separate gates

### 1. Acceptance qualification

`Workbench v0.56.2 Acceptance Qualification` is read-only (`contents: read`, `actions: read`). It may run for the candidate PR and by manual dispatch. It must independently verify:

- exact v0.56.1 predecessor SHA/tree ancestry;
- exact accepted predecessor tag object and peeled commit;
- absence of the v0.56.2 accepted target tag;
- exact post-merge v0.56.1 Actions run and artifact digest;
- full Workbench Release build;
- embedded provenance evidence/admission semantics;
- plain-Russian no-authority presentation;
- disabled historical in-app v0.55.2 publication route;
- fixed publication workflow source boundary;
- clean repository after qualification.

A GREEN qualification does not create an accepted tag and is not publication authority.

### 2. Accepted-tag publication

`Workbench v0.56.2 Publish Accepted` is manual `workflow_dispatch` only. The operator must select `main`, provide the exact current candidate SHA, and type exactly:

`PUBLISH_WORKBENCH_V0.56.2_ACCEPTED`

Before the first write effect it reruns the evidence/build/semantic qualification and reads canonical remote `main` again. Any SHA/tag/evidence drift refuses publication.

The only permitted Git write is creation and push of:

`refs/tags/workbench-v0.56.2-accepted`

pointing to the exact human-confirmed canonical `main` SHA.

The gate does **not** push `main`, force, accept an arbitrary remote/ref/tag, create a GitHub Release, retry automatically, mutate source, invoke a model, execute a runtime, acquire/materialize an artifact, or create action/successor authority.

After the tag write it reads back both the annotated tag object and peeled commit and verifies canonical `main` still equals the confirmed SHA. It emits a separate publication receipt artifact.

## Version identity

`v0.56.1` remains the exact human-tested provenance-review product frontier.

`v0.56.2` is a fresh successor identity because the bounded acceptance closure is additional source content. Do not create `workbench-v0.56.1-accepted` retroactively.

## Boundary

```text
qualified source != accepted release
acceptance qualification != publication
workflow dispatch != automatic authority
provenance != truth
provenance != authority
human review != authorization ceremony
accepted predecessor != automatic successor authority
merge != accepted-tag publication
Trigger != Authorization
```
