# Matawaka Workbench — Development Audit & Roadmap

## Accepted frontier policy

The default branch `main` is the remotely published accepted source frontier. Its exact version is the single `workbench-v<version>-accepted` tag at `main` HEAD.

Permanent roadmap text does not hard-code the previous release as the current accepted state. Exact release/predecessor history remains in `PATCH-v*.md`, Git tags and issues.

## Established capabilities

Historical work established:

- bounded local analysis and semantic/runtime separation;
- visible liveness and evidence/authority receipts;
- self-hosted Workbench update/build/launch with fail-closed rollback;
- explicit local acceptance and fixed fast-forward-only source publication;
- active-surface consolidation without evidence erasure;
- one-session Workbench update sequencing over typed maintenance sub-gates;
- successor-generic post-publication Maintenance Lifecycle Receipt;
- real-successor qualification proving that lifecycle evidence composition is reusable without release-specific lifecycle constants.

The stable Workbench maintenance chain remains:

```text
Update Workbench
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
-> optional Lifecycle receipt
```

## Lifecycle qualification — closed

Initial self-lifecycle result:

`PASS_BOUNDED`

Initial successor-reuse result before stabilization:

`LIFECYCLE_NEEDS_ADAPTER`

The stabilization patch removed release-bound lifecycle target/predecessor constants, decoupled publisher parent ownership and made permanent docs lifecycle-state-neutral.

A real successor transition then completed with exact derived current/predecessor accepted tags, checkpoint-bound Self-test, matching orchestrator/publication evidence and clean state, producing `Complete=true` without manual artifact reconciliation.

Final observed qualification outcome:

`LIFECYCLE_REUSABLE`

This outcome is bounded to evidence composition. It does not authorize automatic lifecycle execution.

```text
Lifecycle Reusable != Lifecycle Automatic
Generic Evidence Routing != Trust Discovery
Summary != Authority
```

## Current product demand — reduced surface + local applications

The next feature line is justified by explicit operator demand, not by architectural novelty:

1. keep only controls used in the normal maintenance workflow;
2. remove persistent Agent/git-fetch checkboxes from the active UI;
3. allow Workbench to update other already-registered local applications with the same evidence/authority discipline.

### Active product surface target

Only eight primary actions:

- Update Workbench;
- Launch candidate;
- Update local app;
- Self-test;
- Accept;
- Publish accepted;
- Lifecycle receipt;
- Stop.

Historical JSON/agent/catalog/recovery capabilities remain source/evidence history and hidden compatibility surfaces.

`Historical Capability != Permanent UI Obligation`

### Local Application Maintenance

Managed apps are restricted to:

`<WorkspaceRoot>/Apps/<ApplicationId>`

and must already possess `.matawaka-app.json` identity/version evidence.

Local app updates use a local exact-manifest ZIP and permit only fresh-preview-bound Add/Replace operations under the fixed app root with predecessor backups, target verification and rollback on failure.

No network/package download, installer execution, app auto-launch, Git, registry/service/environment mutation or arbitrary root selection is admitted.

```text
Package Validity != Mutation Authority
Local App Update != App Launch
Managed Root != Arbitrary Target Root
Initial Registration != Update Authority
```

## Qualification after local-app feature

Do not treat one successful Workbench build as proof that the local-app updater is operationally useful.

After the feature is accepted, useful successor evidence should come from a real registered application update package. Possible outcomes:

- `LOCAL_APP_UPDATE_REUSABLE` — exact managed-app update succeeds and receipt/rollback boundaries are adequate;
- `LOCAL_APP_UPDATE_NEEDS_ADAPTER` — app-specific layout/process constraints require a bounded adapter;
- `LOCAL_APP_REGISTRATION_REQUIRED` — useful existing apps cannot enter the managed root without a separately reviewed adoption boundary;
- `LOCAL_APP_UPDATE_NOT_REQUIRED` — existing app update mechanisms are already sufficient.

Negative outcomes are valid and should prevent building a general installer without evidence.

## Residual stabilization backlog

Review only evidence-backed debt:

- whether release-specific Self-test/checkpoint/publisher successor wrappers should be generalized or remain explicit version boundaries;
- whether hidden legacy compatibility fields can be removed safely from `MainWindow.xaml.cs` without weakening run-state behavior;
- whether a separately bounded **Register local app** function is actually needed;
- whether a read-only update-feed discovery layer is useful after local packages are proven;
- whether external/cross-machine portability has independent product demand.

None of these are automatically authorized implementation tasks.

## Later research directions

Keep separate until independent evidence requires them:

- cross-machine/cross-OS portability;
- trusted producer identity/certificate/trust-anchor models;
- trusted time and real key revocation policy;
- secure external distribution/signing;
- deeper Workbench ↔ UU-AAP reusable composition only where consumer demand is demonstrated.

## Architecture discipline

```text
Product Utility != Core Requirement
Historical Proof != Permanent UI Obligation
UI Consolidation != Evidence Erasure
Maintenance Automation != Authority Collapse
Accepted Local State != Published Remote State
Publication Capability != General Network Capability
Lifecycle Observability != Lifecycle Authority
Local App Maintenance != General Installer Authority
Qualification != Promotion
```