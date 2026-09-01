# Workbench v0.42 — compact operator surface + installed Apps strip

## Accepted predecessor

- commit: `4eb94de604de60909058b51dc3e0a1dc06fd9984`
- tag: `workbench-v0.41.2-accepted`
- semantic predecessor: `0.41.2`

## Visible shell

v0.42 simplifies only the operator-facing shell:

- visible top-level maintenance surface is reduced from 8 to 5 buttons: Update Workbench, Launch candidate, Local apps, Publish accepted, Lifecycle receipt;
- Self-test, Accept and Stop are removed from the visible surface and remain only as collapsed, non-click-routed compatibility bindings required by older partial-class code;
- manual button removal does **not** remove first-boot validation or automatic local acceptance: those remain inside the one-confirmation Update Workbench bootstrap;
- two-word top-level button labels are rendered on two lines and use narrower button widths;
- Workspace and Catalog fields are hidden from the normal shell while their stored values and internal usage remain unchanged;
- registered local applications are observed read-only from `Workspace/Apps/<ApplicationId>/.matawaka-app.json` and shown as `ApplicationId · Version` chips;
- `Find in output` moves below the output tabs;
- progress/status moves to the absolute bottom, with status text rendered over a low-opacity ProgressBar;
- positive status prefixes (`COMPLETED`, `PASS`, `PASSED`, `SUCCESS`, `VALID`) render green; error prefixes (`ERROR`, `FAILED`, `INVALID`) render red; warning prefixes (`WARNING`, `WARN`, `CANCELLED`) render gold. Color is presentation only.

## Invariants

- `Manual button removal != validation removal`.
- `Hidden path setting != forgotten path setting`.
- `Installed Apps list = observation, not app authority`.
- `Status color != status semantics`.
- `Progress overlay != mutation or authority`.
- accepted v0.41.2 search/focus, transition bootstrap, update engine, Local Apps maintenance and non-App runtimes remain predecessor behavior.

## Lifecycle

- semantic Version: `0.42.0`
- target tag: `workbench-v0.42-accepted`
- exact parent: `4eb94de604de60909058b51dc3e0a1dc06fd9984 / workbench-v0.41.2-accepted`
- Update Workbench remains the one-confirmation transition path with automatic first-boot validation + local Accept on PASS;
- Publish accepted remains explicit;
- Lifecycle receipt remains explicit.

## Non-effects

No new local-app registration/update/launch authority, no output/JSON mutation, no clipboard effect, no catalog mutation, no Agent Execute/ActionPermit, no arbitrary network/process authority, no Stable Core/interface-registry promotion.
