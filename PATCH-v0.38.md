# Workbench v0.38 — Local Apps explicit chooser + package-build receipt persistence

## Exact predecessor

- accepted commit: `3e3fa16c51b1d4674975033a8bea59f2701195e8`
- accepted tag: `workbench-v0.37.1-accepted`
- target semantic version: `0.38.0`
- target accepted tag: `workbench-v0.38-accepted`

## Evidence-driven reason

Real-host package-builder qualification completed successfully, but exposed two operator/evidence weaknesses that do not require new execution authority:

1. the registered-app chooser used generic `YES / NO / CANCEL`, mapping YES to Update and NO to Build;
2. the builder persisted the generated ZIP, but its typed success receipt lived only in the Local Apps output, making later evidence recovery unnecessarily indirect.

v0.38 repairs those two surfaces only.

## Product change

The active top-level surface remains exactly eight buttons and zero persistent authority checkboxes.

For a registered app, **Local apps** now opens a dedicated chooser with exact labels:

- `Update from package`
- `Build update package`
- `Cancel`

No effectful action is default-selected. Opening/cancelling the chooser has no package/update/launch effect.

After a successful package build:

- the existing v0.37 builder still derives predecessor hashes from actual registered bytes;
- the existing updater Preview still must accept the generated ZIP before builder success;
- v0.38 independently rechecks surviving package SHA-256 and embedded manifest SHA-256;
- the same typed `LocalApplicationPackageBuilderReceipt` is written as UTF-8 JSON under `Workbench/artifacts/local-app-packages`;
- the receipt is parsed back and exact app/current/target/status/package/manifest/no-effect bindings are required;
- Local Apps output exposes `PackageBuildReceiptPath`.

## Preserved boundaries

```text
Explicit Action Label != Generic Dialog Button Semantics
Artifact Persistence != Receipt Persistence
Candidate Source != Managed Application
Builder Preview != Package Write Authority
Package Write != Update Authority
Build Package != Update App != Launch App
```

No candidate import, package auto-apply, app launch, network/download, arbitrary filesystem root, Git/registry/service/environment/Agent Execute authority is added.

## Qualification target

Windows CI must prove:

- Release build succeeds with zero errors;
- active top-level UI remains 8 buttons / 0 checkboxes;
- chooser exact labels exist and no default action is effectful;
- v0.37.1 candidate/managed-root role guard remains intact;
- compiled real fixture registers an app, builds a package, receives existing updater Preview READY, persists the builder receipt JSON, parses it back, and verifies package/manifest binding;
- package build still does not mutate/update/launch the application;
- non-App implementation projects are unchanged;
- a source-only v0.38 package is emitted over exact accepted v0.37.1.

## Acceptance lifecycle

`Update Workbench -> separate Launch candidate -> separate Self-test -> separate Accept -> separate Publish accepted -> separate Lifecycle receipt`

No earlier receipt creates later authority.
