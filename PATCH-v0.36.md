# Matawaka Workbench v0.36 — Local Apps Registration & Contextual Manager

Status: source candidate over exact accepted `workbench-v0.35.1-accepted`.

## Exact predecessor

- commit `bf4c59e251c4e9642cc0ca7085e5c3af24622707`
- tag `workbench-v0.35.1-accepted`
- parent `689cdf5ef2f9f403efe09bb251c91da1c5951ec6`

## Why this successor exists

The bounded v0.35 local-app updater requires a valid `.matawaka-app.json`. Real operator evidence showed that no managed app currently had one, making registration a concrete missing prerequisite.

v0.36 does **not** add another top-level button. It replaces visible `Update local app` with contextual **Local apps** while keeping exactly eight active buttons and zero persistent checkboxes.

## Contextual Local Apps flow

1. select one direct child of `<WorkspaceRoot>\Apps`;
2. if `.matawaka-app.json` is absent → registration path;
3. if identity exists → bounded update ZIP path.

Each path keeps a separate preview and separate explicit confirmation.

`Contextual UI != Authority Collapse`

## Registration boundary

`LocalApplicationRegistrationService` can only register an existing direct managed child. It does not import, copy or move applications.

Preview binds:

- ApplicationId from folder name;
- exact file inventory (max 4096 files / 2 GiB);
- normalized relative paths;
- SHA-256 and bytes per file;
- deterministic tree SHA-256;
- proposed identity `baseline-<first16-tree-digest>`.

The baseline is an observed-byte token, not a vendor/upstream version assertion.

After explicit confirmation the service freshly repeats the inventory and may create only `.matawaka-app.json`. It then verifies the identity and confirms all pre-existing app bytes remain unchanged. Failure removes the identity and verifies the original tree baseline.

Success:

`LOCAL_APPLICATION_REGISTERED_UPDATE_AUTHORITY_NOT_CREATED`

## Existing bounded updater

Registered apps continue through the v0.35 `LocalApplicationMaintenanceService`:

- exact local package manifest/payload;
- current/target SHA-256 bindings;
- fixed managed root;
- Add/Replace only;
- fresh preview before mutation;
- backup + verified rollback;
- no auto-launch.

## Qualification fixture

Windows CI must create an isolated temporary workspace with `Apps/demo.app`, then invoke compiled product code to prove:

1. registration Preview is READY while identity is absent;
2. explicit Register writes only `.matawaka-app.json` and a Workbench receipt;
3. pre-existing fixture bytes/tree digest are unchanged;
4. second registration is refused;
5. nested/outside root selection is refused;
6. CI builds a matching local-app update ZIP from the registered baseline;
7. existing updater Preview/Apply succeeds to target version;
8. target bytes/identity are exact;
9. `AppLaunchPerformed=false` and no network/Git/installer effects occur.

The fixture is temporary CI evidence only and is not included in the Workbench update package.

## v0.36 acceptance chain

- Self-test schema `matawaka.workbench-acceptance-receipt/v0.36`, semantic Version `0.36.0`;
- local target `workbench-v0.36-accepted` over exact v0.35.1;
- fixed publication remains separate and non-force;
- Lifecycle v2 remains separate post-publication evidence.

## Invariants

```text
Register Local App != Import App
Register Local App != Update App
Register Local App != Launch App
Registration Baseline != Vendor Version Claim
Package Validity != Mutation Authority
Local App Update != App Launch
Local App Authority != Workbench Acceptance Authority
Contextual UI != Authority Collapse
```

No Runtime/Protocol/AgentHost/Engine/Catalog/SemanticHost change, no Stable Core promotion, no general installer/network/filesystem capability.
