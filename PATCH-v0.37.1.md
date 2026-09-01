# Workbench v0.37.1 — Candidate/Managed Root Role Separation

Patch-level stabilization over accepted `workbench-v0.37-accepted` at `0d20e3bbe7c28b48cac3ef97b903b4a3a6176521`.

## Evidence

Real-host package-builder qualification accidentally placed target-candidate bytes under `Workspace/Apps/demo.app`. Because `.matawaka-app.json` was absent, accepted v0.37 registration treated the directory as an unregistered managed app and created a new baseline identity even though `.matawaka-target.json` was present.

Observed role collision:

- managed root contained `.matawaka-target.json`;
- target `feature.txt` and `hello.txt` bytes were present;
- registration produced `baseline-5460171dc3941602`.

## Stabilization

New invariant:

`Candidate Source != Managed Application`

Before historical registration runs, `LocalApplicationManagedRoleGuardV0371Service` checks the exact selected direct child of `Workspace/Apps`. Presence of `.matawaka-target.json` as file or directory causes a fail-closed refusal with guidance to move target candidate bytes to `Workspace/AppCandidates/<ApplicationId>`.

The guard is read-only. It does not move/copy/import/delete files and creates no identity/update/package-build authority.

## UI

Still exactly eight active buttons and zero persistent authority checkboxes. `Local apps` uses the role guard only for the unregistered branch; registered Update/Build package behavior remains unchanged.

## Acceptance

v0.37.1 Self-test = accepted v0.37 matrix + role-separation checks.

Local acceptance target:

`workbench-v0.37.1-accepted`

Exact predecessor:

`0d20e3bbe7c28b48cac3ef97b903b4a3a6176521 / workbench-v0.37-accepted`

Publication remains a separate fixed non-force fast-forward/tag decision; Lifecycle V2 remains separate.

## Non-effects

No candidate import feature, no automatic file move/copy, no new local-app update authority, no Runtime/Protocol/AgentHost/Engine/Catalog/SemanticHost change, no network/Git/installer/process/registry/service/Agent Execute expansion, no Stable Core/interface-registry promotion.
