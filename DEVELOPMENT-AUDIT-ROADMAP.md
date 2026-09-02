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

The existing update package is already sparse: only actual Add/Replace payload files plus target `.matawaka-app.json` need to be present. `Export update context` is the planned/implemented bridge that lets another conversation build such a sparse package without receiving unchanged private application bytes merely to reconstruct predecessor SHA-256 bindings.

```text
Package Validity != Mutation Authority
Local App Update != App Launch
Managed Root != Arbitrary Target Root
Initial Registration != Update Authority
Tree Observation != File Mutation
Text Inspection != Execution Authority
Sparse Update != Full Application Copy
```

## v0.46 — Local App Operational Handoff

The next admitted Workbench layer is **Local App Operational Handoff**. It remains inside the existing top-level `Local apps` surface and does not add a fifth primary Workbench button.

### Explicit app launch

A registered application may be launched only after the operator selects one exact `.exe` already under its fixed managed root and confirms a preview bound to relative path, SHA-256, size and installed identity/version. v0.46 supplies zero arguments and writes a local launch receipt.

Workbench does not claim the launched program is sandboxed merely because its launch was bounded.

```text
Registration != Update != Launch
Exact Launch Authority != Authority Over App Behavior
```

### Content-free update context

`Export update context` writes a local JSON containing installed identity/version, file paths, SHA-256, sizes and coarse roles. It contains no application file contents and performs no upload.

Its main consumer is another development chat building `matawaka.local-app-update-package/v1` with only changed/added files.

```text
Update Context != Application Copy
Predecessor Evidence != Disclosure Of Contents
```

### Development source role

Reproducible development source lives separately at:

`<WorkspaceRoot>/AppSources/<ApplicationId>`

A manually extracted source seed may be explicitly bound to a registered app. Binding creates only `.matawaka-source.json` after a fresh bounded inventory; it does not import, copy, move, overwrite or freeze source bytes.

```text
Installed Bytes != Development Sources
Source Binding != Source Mutation Authority
Source Edit != Installed App Mutation
```

### PRIVATE development context

After source binding, the operator may explicitly create one PRIVATE local context capsule containing installed/runtime/evidence bytes, bound development sources, the content-free update context, a handoff manifest and the local read-tool contract.

This is deliberately a disclosure-preparation operation and may duplicate confidential banking/evidence bytes into one ignored local artifact. Workbench never uploads that artifact automatically.

```text
Export Context != Upload Context != Authority to Disclose
Private Context Export != Public Repository Publication
```

For large private applications, the intended later optimization is an immutable private base capsule plus smaller context deltas bound to the base digest. That is a portability/handoff optimization, not cloud synchronization authority.

### Future local content-read primitive

v0.46 establishes a reusable local read service with request/response contracts for roles `installed` and `source`, fixed ApplicationId + relative-path confinement, reparse refusal, bounded chunk reads, full-file SHA-256/size evidence, Base64 response bytes and strict UTF-8 text when possible.

No external connector/network transport is part of v0.46. A later ChatGPT/Workbench tool adapter must invoke this bounded primitive rather than receiving general filesystem authority.

```text
Content Read != File Mutation != Execution
Tool Contract != Transport Authority
Local Read Primitive != Automatic Disclosure
```

## Qualification after local-app feature

Do not treat one successful Workbench build as proof that the local-app updater or operational handoff is useful.

Useful successor evidence should include real registered applications. Possible outcomes:

- `LOCAL_APP_UPDATE_REUSABLE` — exact managed-app update succeeds and receipt/rollback boundaries are adequate;
- `LOCAL_APP_UPDATE_NEEDS_ADAPTER` — app-specific layout/process constraints require a bounded adapter;
- `LOCAL_APP_SOURCE_HANDOFF_REUSABLE` — source seed -> AppSources binding -> private development context gives another conversation enough reproducible development state;
- `LOCAL_APP_READ_TOOL_NEEDS_TRANSPORT_ADAPTER` — local read primitive is correct but a separate connector/transport is still required;
- `LOCAL_APP_PRIVATE_CONTEXT_TOO_HEAVY` — base+delta context should be implemented before routine handoff;
- `LOCAL_APP_UPDATE_NOT_REQUIRED` — existing app update mechanisms are already sufficient.

Negative outcomes are valid and should prevent broad filesystem/import/network authority from being inferred.

## Residual stabilization backlog

Review only evidence-backed debt:

- whether release-specific validation/checkpoint/publisher successor wrappers should be generalized or remain explicit version boundaries;
- whether quarantined hidden compatibility fields can eventually be physically removed without weakening historical run-state behavior;
- whether a read-only update-feed discovery layer is useful after local packages are proven;
- whether base+delta PRIVATE context materially reduces repeated transfer of large confidential evidence;
- what connector/tool transport can expose the v0.46 local read primitive without general filesystem authority;
- whether external/cross-machine portability has independent product demand.

None of the remaining items are automatically authorized implementation tasks.

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
Export Context != Upload Context
Installed Bytes != Development Sources
Content Read != Mutation Authority
Qualification != Promotion
```
