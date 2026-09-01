# Matawaka Workbench — Development Audit & Roadmap

## Accepted frontier policy

The default branch `main` is the remotely published accepted source frontier. Its exact version is the single `workbench-v<version>-accepted` tag at `main` HEAD.

Permanent roadmap text does not hard-code the previous release as the current accepted state. Exact release/predecessor history remains in `PATCH-v*.md`, Git tags and issues.

## What is established

Historical work established:

- bounded local analysis and semantic/runtime separation;
- visible liveness and evidence/authority receipts;
- self-hosted candidate update/build/launch with fail-closed rollback;
- explicit local acceptance and fixed fast-forward-only source publication;
- active-surface consolidation without evidence erasure;
- one-session `Update candidate` sequencing over typed maintenance sub-gates;
- post-publication Maintenance Lifecycle Receipt that can bind one exact completed lifecycle without creating authority.

The stable visible maintenance chain remains:

```text
Update candidate
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
-> optional Lifecycle receipt
```

## Qualification findings after the first complete lifecycle

### Accepted frontier integrity

`PASS`

The accepted lifecycle release was independently verified on GitHub as one successor commit with matching annotated accepted tag and expected product-only delta.

### Self-lifecycle completeness

`PASS_BOUNDED`

One exact update/build → Self-test → checkpoint → publication relation was successfully composed with all relation checks passing while `AuthorityCreated`, `ActionPerformed`, `RetryAuthorized` and `RollbackAuthorized` remained false.

### Successor lifecycle reuse

Initial outcome:

`LIFECYCLE_NEEDS_ADAPTER`

Reason: the first lifecycle service hard-coded its own target version/tag and exact predecessor, so it could prove its own lifecycle but could not assess the next successor transition without another release-specific service.

### Accepted documentation currentness

`STABILIZATION_REQUIRED`

Permanent public docs were candidate-state documents and became stale immediately after acceptance/publication. This was a recurring lifecycle defect rather than a runtime failure.

## Qualification/stabilization patch

The patch-level response is intentionally not a new feature layer.

### Successor-generic lifecycle evidence routing

The lifecycle service now derives its target/predecessor from exact evidence:

```text
current HEAD
+ unique workbench-v<version>-accepted tag at HEAD
+ exact checkpoint at HEAD/tag
+ exact Git parent + unique predecessor accepted tag
+ checkpoint-bound passing Self-test artifact
+ unique matching orchestrator receipt
+ unique matching publication receipt
+ exact SHA-256 bindings + clean state
-> complete lifecycle assessment
```

Missing/ambiguous evidence fails closed; modification time is never a selection rule. Accepted tag discovery routes evidence only and creates no trust or authority.

### Lifecycle-state-neutral public docs

Stable README/START/SECURITY/ROADMAP text no longer embeds candidate status or a prior release as the accepted baseline. Candidate-specific details remain in patch notes/package previews/issues.

`Accepted Source Documentation != Candidate Planning Document`

## Real successor qualification

A patch-level real successor transition is required to qualify the generic adapter. The sequence is:

1. install a bounded patch through the accepted `Update candidate` path;
2. separately Launch candidate;
3. separately Self-test and require PASS;
4. separately accept local checkpoint;
5. separately Publish accepted;
6. run the same generic `Lifecycle receipt` service;
7. independently verify remote main/tag and accepted bytes.

Then classify only from observed evidence:

- `LIFECYCLE_REUSABLE` — generic adapter produces one exact `Complete=true` lifecycle relation on the new successor without manual artifact reconciliation;
- `LIFECYCLE_NEEDS_ADAPTER` — a required direct relation is still missing;
- `LIFECYCLE_AMBIGUOUS` — multiple otherwise qualifying evidence candidates prevent unique binding;
- `LIFECYCLE_NOT_REQUIRED` — aggregate evidence adds no material value over individual receipts.

A negative result is successful qualification evidence and blocks feature inflation.

## Stabilization backlog after qualification

Do not automatically create a new feature release. Review only evidence-backed residual debt:

- whether release-specific Self-test/checkpoint/publisher successor wrappers should be generalized or remain explicit version boundaries;
- whether hidden legacy compatibility bindings can be removed safely from `MainWindow.xaml.cs` without weakening run-state liveness;
- whether lifecycle evidence remains useful after more than one real successor;
- whether external/cross-machine portability has independent product demand.

These are not authorized implementation tasks merely because they exist as possible improvements.

## Later research directions

Keep separate until independent evidence requires them:

- cross-machine/cross-OS portability;
- trusted producer identity/certificate/trust-anchor models;
- trusted time and real key revocation policy;
- secure external distribution/signing;
- deeper Workbench ↔ UU-AAP reusable composition only where consumer demand is demonstrated.

## Architecture discipline

```text
Product utility != Core requirement
Historical proof != Permanent UI obligation
UI consolidation != Evidence erasure
Maintenance automation != Authority collapse
Accepted local state != Published remote state
Publication capability != General network capability
Lifecycle observability != Lifecycle authority
Generic Evidence Discovery != Trust Discovery
Qualification != Promotion
Patch Release != Feature Layer
```
