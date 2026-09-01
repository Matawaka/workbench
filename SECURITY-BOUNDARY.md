# Matawaka Workbench — Security boundary

This document describes stable security/authority boundaries. Exact release identities belong to accepted tags, package previews and `PATCH-v*.md` history rather than permanent release-planning prose.

The established semantic/runtime boundary remains unchanged: fixed verified SemanticHost, restricted Low-integrity token, Windows Job Object, runtime attestation before semantic input, read-only proposal behavior and denied Execute control.

These rules apply across accepted transitions and do not infer authority from version labels.

## Active-surface rule

The active Workbench UI exposes only the currently used maintenance controls. Historical JSON/agent/catalog/recovery controls and their evidence remain in source/history but are not persistent top-level authority controls.

Persistent `Agent enabled` and `Allow git fetch` checkboxes are not part of the active surface. Self-test is an explicit human action and runs the existing bounded acceptance matrix directly; removing the persistent checkbox does not create Agent Execute authority. Catalog fetch remains code/history but is not a top-level visible effect.

```text
Hidden Control != Deleted Capability
UI Simplification != Evidence Erasure
Persistent Checkbox Removed != Persistent Authority Granted
Self-test Click != Agent Execute
```

## Maintenance distinctions

```text
One Update Workbench Confirmation != Authority Collapse
Successful Build != Candidate Launch
Candidate Launch != Self-test
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Remote Publication Authority
Publication Success != Lifecycle Authority
Lifecycle Summary != Lifecycle Authority
Local App Package Validity != Local App Mutation Authority
Local App Update != App Launch
Managed App Root != Arbitrary Filesystem Root
Observed Sequence != Authorized Sequence
Accepted Tag Discovery != Trust Discovery
Receipt Binding != Automatic Transition
```

No receipt from one stage silently authorizes a later stage.

## Workbench update/build boundary

**Update Workbench** sequences existing typed package intake, staging-only materialization, staged source plan and exact source apply/build services. Each retains fresh evidence checks and a receipt. The orchestrator stops at:

`CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`

It does not call candidate launch, Self-test, checkpoint or publisher services.

## Separate Workbench candidate launch

**Launch candidate** remains a separate exact-executable gate. It consumes the exact build receipt, rechecks candidate/SemanticHost digests and launches only the receipt-bound local Workbench executable.

`Update Workbench success != Launch authority`.

## Self-test boundary

Self-test is a read-only acceptance matrix with respect to update/build/launch/checkpoint/publication/lifecycle/local-app-update effects. The explicit Self-test click itself is the human test-authority action; a persistent Agent-enabled checkbox is not required. Self-test may enable only the pre-existing bounded test context needed to exercise read-only propose/deny controls and does not grant Agent Execute.

`Contract Check != Effect Exercise`.

## Local checkpoint boundary

Local Workbench acceptance requires a passing in-process Self-test receipt, exact acceptance artifact/running executable digest binding, exact accepted predecessor HEAD/tag and a build-source manifest matching the full changed source set.

The checkpoint gate may perform only its fixed local `git add/commit/tag` transaction after explicit confirmation. It creates no push/network/publication/lifecycle/local-app-update authority.

## Fixed accepted-source publication boundary

**Publish accepted** is a separate explicit human maintenance network gate with one destination only:

```text
remote = github-workbench
url = https://github.com/Matawaka/workbench.git
branch = refs/heads/main
tag = exact locally accepted workbench-v<version>-accepted
```

Remote main must be exact local parent or exact local HEAD. A conflicting main/tag fails closed. Branch publication is non-force exact-head fast-forward only. Final remote main/tag readback must equal local accepted HEAD and local HEAD/working tree must remain unchanged.

Publication creates no local-app update authority, arbitrary remote, force-push, tag movement, catalog mutation, Agent Execute, ActionPermit or general Workbench network authority.

## Local application maintenance boundary

`Update local app` is a separate local filesystem maintenance authority with a fixed root model.

Eligible root:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

The package cannot supply an absolute target root. `ApplicationId` is a bounded token and the root is derived by Workbench.

A managed app must already contain `.matawaka-app.json` with:

- schema `matawaka.local-app-identity/v1`;
- exact ApplicationId;
- current Version.

The update package must use schema `matawaka.local-app-update-package/v1` and contain exactly:

- `local-app-update-manifest.json`;
- manifest-declared `payload/...` file entries.

### Read-only preview requirements

Before mutation, Workbench requires:

- bounded local ZIP size/file count;
- exact ZIP entry set with no duplicate/case-colliding names;
- safe non-rooted/non-traversing relative paths;
- application root under the fixed `Apps` root;
- no application-root/path-segment reparse-point escape;
- exact current `.matawaka-app.json` identity/version;
- exact SHA-256 for every payload file;
- exact `CurrentSha256` for every replacement file;
- no predecessor digest for Add paths;
- target `.matawaka-app.json` included and matching app id/target version;
- all requested-effect flags false: network/process launch/installer/registry/service/environment/Agent Execute.

Preview creates no mutation authority by itself.

### Explicit apply boundary

After user confirmation, Workbench freshly reruns Preview and requires an equivalent package/app/file relation before any write.

The only permitted effects are:

1. backup exact predecessor bytes for replacement paths under ignored Workbench-local backup storage;
2. Add/Replace exact manifest-declared files under the one fixed managed app root;
3. use temporary files and verify target SHA-256 before replacement;
4. verify all final file digests and target identity/version;
5. write one local app-update receipt.

Delete is not supported in this boundary.

On failure after backup, Workbench restores replacement bytes, removes added files and verifies predecessor identity/digests before reporting bounded failure.

### Explicit non-effects

The local-app updater cannot:

- download a package or access network;
- call Git;
- execute MSI/EXE/script installers;
- launch the updated application;
- delete existing application paths;
- mutate Windows registry, services or environment variables;
- choose a target outside `Workspace\Apps\<ApplicationId>`;
- mutate Workbench source;
- mutate Matawaka catalog repositories;
- create Agent Execute or ActionPermit;
- claim canonical UU-AAP conformance or Stable Core membership.

```text
Package Validity != Mutation Authority
Local App Update != App Launch
Explicit Update Confirmation != General Filesystem Authority
Initial App Registration != App Update Authority
```

Initial adoption/registration of an arbitrary existing application remains out of scope until separately evidenced and reviewed.

## Successor-generic Maintenance Lifecycle Receipt boundary

The lifecycle service is post-publication evidence composition only. It may read Workbench-local ignored artifacts and a fixed read-only Git observation allowlist. It may not invoke Update Workbench, source apply/build, candidate launch, Self-test, local checkpoint, publication or local-app update services.

It derives the current accepted relation from exact current tag/checkpoint/acceptance/orchestrator/publication evidence, binds SHA-256 of each artifact and fails closed on missing or ambiguous matches. Modification time is never a selection rule.

After a complete assessment, explicit confirmation may write one local lifecycle receipt with:

- `AuthorityCreated=false`;
- `ActionPerformed=false`;
- `RetryAuthorized=false`;
- `RollbackAuthorized=false`.

```text
Generic Evidence Discovery != Authority Discovery
Accepted Tag Discovery != Trust Discovery
Missing/Ambiguous Artifact != Inferred Success
Lifecycle Receipt != ActionPermit
```

## Prohibited inference

Successful Workbench/local-app maintenance does not establish producer identity, trust, legal authority, canonical UU-AAP conformance, Stable Core membership, or general execution/network/filesystem authority.
