# Workbench v0.38.1 — Explicit Chooser Layout Stabilization

Exact predecessor:

- commit `7c749b357f5c4095fe968f3fa18dba3cd1b52339`
- tag `workbench-v0.38-accepted`

Target:

- semantic version `0.38.1`
- tag `workbench-v0.38.1-accepted`

## Evidence

Real-host issue #19 showed the v0.38 chooser rendered `Update from package` and `Build update package`, but the third `Cancel` action was clipped below the client area. Package-build receipt persistence itself passed.

## Change

`LocalAppsActionDialogV038` no longer uses fixed outer height `245`. It uses:

- bounded width;
- `MinHeight = 300`;
- `SizeToContent = SizeToContent.Height`;
- `ResizeMode = NoResize`;
- unchanged exact action labels;
- unchanged initial `Choice=Cancel`;
- no default effectful button.

`Explicit Action Label != Hidden/Clipped Action`

## Preserved bytes/semantics

No intended change to:

- `LocalApplicationPackageBuilderService`;
- `LocalApplicationMaintenanceService`;
- `LocalApplicationRegistrationService`;
- `LocalApplicationManagedRoleGuardV0371Service`;
- `LocalApplicationPackageBuildReceiptStoreV038Service`;
- package/update/launch authority separation.

Top-level Workbench surface remains exactly 8 buttons and 0 persistent authority checkboxes.

## Lifecycle

v0.38.1 adds separate Self-test / local checkpoint / fixed publication contracts over exact accepted v0.38. Lifecycle receipt remains a separate evidence-composition action.

No package build, app update, app launch, network, Agent Execute, Stable Core or interface-registry authority is created by this patch merely existing or being accepted.
