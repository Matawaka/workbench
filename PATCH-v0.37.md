# Matawaka Workbench v0.37 — Local App Update Package Builder

Status: candidate source layer over exact accepted `workbench-v0.36-accepted`. Candidate source does not change accepted remote `main`.

## Exact predecessor

- commit `8f8bac01661c0b5614422c3708f1afb78a483c8b`
- tag `workbench-v0.36-accepted`

## Evidenced need

Real-host qualification of v0.36 proved Register → Update on Windows. It also exposed an avoidable manual package-authoring defect: a hand-built update fixture reconstructed `.matawaka-app.json` with different line endings, so semantic JSON matched while exact predecessor SHA-256 did not. The existing updater correctly refused the package without mutation.

This yields the product requirement:

`Semantic Equality != Byte Equality`

Exact predecessor hashes should be derived from actual registered bytes, not manually reconstructed.

## Product behavior

The top-level UI remains exactly eight buttons and zero persistent authority checkboxes. `Local apps` remains the single contextual control.

For a registered app it now offers:

- Update from package — existing v0.35 updater unchanged;
- Build update package — new v0.37 builder;
- Cancel.

## Fixed candidate root

Desired target bytes must be under:

`<WorkspaceRoot>/AppCandidates/<ApplicationId>/`

with `.matawaka-target.json` schema `matawaka.local-app-target/v1` containing exact ApplicationId and TargetVersion.

No arbitrary candidate folder is accepted.

## Builder Preview

Preview validates registered and candidate roots, reparse/path/file-count/byte bounds, reads current hashes from the actual registered app, target hashes from candidate bytes, derives Add/Replace/NoOp, refuses implicit Delete, generates target identity bytes, and synthesizes the exact `matawaka.local-app-update-package/v1` manifest in memory.

`Builder Preview != Package Write Authority`

## Explicit package write

After separate confirmation the builder freshly recomputes both sides and requires exact equivalence, writes one ZIP only under `Workbench/artifacts/local-app-packages`, then immediately submits the generated ZIP to the existing `LocalApplicationMaintenanceService.PreviewAsync`.

Builder success requires the existing updater Preview to return READY with matching app/current/target/package/manifest identity.

`Builder Success => Existing Updater Preview READY`

No app update, registration or launch occurs while building the package.

## Authority invariants

```text
Package Write != Update Authority
Build Package != Update App != Launch App
Candidate Root != Arbitrary Read Root
Generated Manifest != Mutation Permit
Builder Success != Update Success
```

No network, Git, installer/script, process launch, registry/service/environment mutation, catalog mutation, Agent Execute, ActionPermit or Stable Core promotion.

## Acceptance

v0.37 Self-test reuses the complete accepted v0.36 matrix and adds only offline builder checks. Local checkpoint target is `workbench-v0.37-accepted`; publication and Lifecycle V2 remain separate.

## Required CI qualification

A Windows compiled fixture must:

1. create an unregistered `Apps/demo.app`;
2. register it with the real registration service;
3. create `AppCandidates/demo.app` + `.matawaka-target.json`;
4. build the package with the real builder;
5. require existing updater Preview READY;
6. separately apply the package with the existing updater;
7. verify target bytes/version and no auto-launch;
8. separately prove omission of a current file is refused as implicit Delete.

Only after this and normal v0.37 Self-test/Accept/Publish/Lifecycle may v0.37 become accepted.
