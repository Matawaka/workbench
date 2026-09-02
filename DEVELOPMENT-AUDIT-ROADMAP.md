# Matawaka Workbench — Development Audit & Roadmap

## Accepted frontier policy

The default branch `main` is the remotely published accepted source frontier. Its exact version is the single `workbench-v<version>-accepted` tag at `main` HEAD.

Permanent roadmap text does not hard-code the previous release as the current accepted state. Exact release/predecessor history remains in `PATCH-v*.md`, Git tags and issues.

## Established capabilities

Historical work established:

- bounded local analysis and semantic/runtime separation;
- visible liveness and evidence/authority receipts;
- self-hosted Workbench update/build/handoff with fail-closed rollback;
- explicit local acceptance and fixed fast-forward-only source publication;
- active-surface consolidation without evidence erasure;
- one-confirmation Workbench update sequencing over typed maintenance sub-gates;
- successor first-boot validation with one-shot automatic local Accept on PASS;
- successor-generic post-publication Maintenance Lifecycle Receipt;
- real-successor qualification proving that lifecycle evidence composition is reusable without release-specific lifecycle constants;
- registered local-app maintenance/package tooling under a fixed managed root;
- clickable installed-app entry points with bounded read-only tree observation;
- closable application/file inspection tabs and bounded double-click text inspection;
- a four-action normal operator surface with historical controls kept outside the active UI.

The current normal Workbench maintenance chain is:

```text
Update Workbench
-> one explicit human confirmation
-> bounded exact source apply/build
-> one-shot candidate launch/handoff
-> successor first-boot validation
-> automatic local Accept only if validation PASS
-> separate Publish accepted
-> optional Lifecycle receipt
```

The candidate launch, validation and local acceptance stages remain distinct in authority/evidence semantics even though they are no longer separate normal-workflow buttons.

```text
Automatic Sequencing != Authority Collapse
Internal Stage != Permanent UI Button
```

## Lifecycle qualification — closed

Initial self-lifecycle result:

`PASS_BOUNDED`

Initial successor-reuse result before stabilization:

`LIFECYCLE_NEEDS_ADAPTER`

The stabilization patch removed release-bound lifecycle target/predecessor constants, decoupled publisher parent ownership and made permanent docs lifecycle-state-neutral.

A real successor transition then completed with exact derived current/predecessor accepted tags, checkpoint-bound validation, matching orchestrator/publication evidence and clean state, producing `Complete=true` without manual artifact reconciliation.

Final observed qualification outcome:

`LIFECYCLE_REUSABLE`

This outcome is bounded to evidence composition. It does not authorize automatic lifecycle execution.

```text
Lifecycle Reusable != Lifecycle Automatic
Generic Evidence Routing != Trust Discovery
Summary != Authority
```

## Current product surface

The normal operator surface is intentionally small. Only four primary maintenance actions are active:

- `Update Workbench`;
- `Local apps`;
- `Publish accepted`;
- `Lifecycle receipt`.

`Self-test`, `Accept`, `Stop` and `Launch candidate` are retired from the active surface. Historical JSON/agent/catalog/recovery controls remain source/evidence history and compatibility bindings only where older code still requires object names.

Workspace and Catalog values remain internal persisted state because accepted maintenance/runtime services still consume them. They are not operator-facing fields.

```text
Historical Capability != Permanent UI Obligation
Hidden Compatibility Binding != Operator Authority
Workspace/Catalog Hidden != Workspace/Catalog Undefined
UI Removal != Evidence Erasure
```

## Local Application Maintenance and Inspection

Managed apps are restricted to:

`<WorkspaceRoot>/Apps/<ApplicationId>`

and must already possess `.matawaka-app.json` identity/version evidence.

Local app updates use a local exact-manifest ZIP and permit only fresh-preview-bound Add/Replace operations under the fixed app root with predecessor backups, target verification and rollback on failure.

Installed apps are observable from the main Workbench surface. An app entry opens a closable read-only tree tab. Explicit double-click on a represented file may open a closable read-only text tab under bounded path/reparse/size/encoding checks.

No network/package download, installer execution, app auto-launch, Git, registry/service/environment mutation or arbitrary root selection is admitted by local-app maintenance or inspection.

```text
Package Validity != Mutation Authority
Local App Update != App Launch
Managed Root != Arbitrary Target Root
Initial Registration != Update Authority
Tree Observation != File Mutation
Text Inspection != Execution Authority
```

## Qualification after local-app feature

Do not treat one successful Workbench build as proof that the local-app updater is operationally useful.

Useful successor evidence for mutation should still come from a real registered application update package. Possible outcomes:

- `LOCAL_APP_UPDATE_REUSABLE` — exact managed-app update succeeds and receipt/rollback boundaries are adequate;
- `LOCAL_APP_UPDATE_NEEDS_ADAPTER` — app-specific layout/process constraints require a bounded adapter;
- `LOCAL_APP_REGISTRATION_REQUIRED` — useful existing apps cannot enter the managed root without a separately reviewed adoption boundary;
- `LOCAL_APP_UPDATE_NOT_REQUIRED` — existing app update mechanisms are already sufficient.

Read-only app tree/text inspection is a separate accepted product capability and does not itself prove mutation/update reuse.

Negative outcomes are valid and should prevent building a general installer without evidence.

## Residual stabilization backlog

Review only evidence-backed debt:

- whether release-specific validation/checkpoint/publisher successor wrappers should be generalized or remain explicit version boundaries;
- whether quarantined hidden compatibility fields can eventually be physically removed without weakening historical run-state behavior;
- whether a separately bounded **Register local app** function is actually needed;
- whether a read-only update-feed discovery layer is useful after local packages are proven;
- whether external/cross-machine portability has independent product demand.

The current v0.45 direction is quarantine and contract enforcement, not physical erasure of historical code. None of the remaining items are automatically authorized implementation tasks.

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
