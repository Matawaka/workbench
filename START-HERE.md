# Start here — Matawaka Workbench

This page describes the stable operator path. Exact release-specific predecessor/target identities are shown by update previews, accepted tags and `PATCH-v*.md` history rather than hard-coded here.

## Active controls

The normal window intentionally exposes only:

- **Update Workbench**
- **Launch candidate**
- **Local apps**
- **Self-test**
- **Accept**
- **Publish accepted**
- **Lifecycle receipt**
- **Stop**

There are no persistent Agent or git-fetch checkboxes.

## Workbench update lifecycle

Starting with v0.40, **Update Workbench** is the normal one-confirmation transition path:

1. choose the source-only ZIP;
2. inspect exact package SHA, predecessor and target;
3. confirm the transition once;
4. existing typed gates freshly plan → materialize → staged-plan → exact source apply/build;
5. Workbench creates one exact, expiring, one-shot bootstrap lease;
6. it automatically launches only the exact build-receipt-bound candidate;
7. existing v0.39 handoff re-verifies persisted launch receipt, live PID, exact process-image path and candidate SHA;
8. only after that evidence is persisted, the predecessor closes its own window;
9. the exact launched successor PID may claim the lease once on its first boot;
10. it automatically runs the normal Self-test;
11. only when `Passed=true`, it creates the normal local accepted checkpoint/tag automatically;
12. **Publish accepted** remains a separate explicit action;
13. **Lifecycle receipt** remains a separate explicit action after publication.

The top-level **Launch candidate** button remains a manual fallback. Manual launch does not mint a bootstrap lease and therefore does not trigger automatic first-boot Self-test/Accept.

A claim is persisted before automatic Self-test begins. Any failure, crash, stale evidence, wrong PID/path/SHA, or checkpoint error terminates the automatic path as `FAILED_NO_RETRY`; a later manual Self-test/Accept remains possible, but automatic retry authority is never created.

Activation boundary: a newly installed release cannot retroactively change the already-running predecessor executable. Therefore the first installation of v0.40 from v0.39.1 still follows the old v0.39.1 manual Launch/Self-test/Accept path. Once v0.40 itself is running, its next successor transition exercises the one-confirmation bootstrap.

```text
One Update Confirmation != General Future Launch Authority
Auto Launch != Candidate Acceptance
First Boot Trigger != Reusable Acceptance Authority
Self-test PASS Required Before Auto Accept
Failed Self-test != Retry Authority
Manual/Repeated Launch != Bootstrap Launch
Bootstrap Lease Consumed Once
Predecessor Self-Close != External Process-Kill Authority
Accept != Publish
Publish != Lifecycle Authority
```

## Local apps

`Local apps` is contextual. Choose one direct child of:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

### Unregistered app

If `.matawaka-app.json` is absent, Workbench offers **Register**. Registration inventories the existing bytes, derives a deterministic `baseline-*` identity, freshly rechecks the bytes, then creates only `.matawaka-app.json`.

A directory carrying `.matawaka-target.json` is candidate-role content and is refused for registration under `Workspace\Apps`.

Expected status:

`LOCAL_APPLICATION_REGISTERED_UPDATE_AUTHORITY_NOT_CREATED`

### Registered app

If `.matawaka-app.json` exists, Workbench opens an explicit action chooser with three directly labelled buttons:

- **Update from package**
- **Build update package**
- **Cancel**

Opening or cancelling the chooser performs no action. There is no YES/NO semantic mapping and no default effect button.

#### Update from package

Choose an existing `matawaka.local-app-update-package/v1` ZIP. Workbench validates exact current SHA-256 bindings and target payload digests, freshly revalidates, backs up predecessor bytes, applies Add/Replace only, verifies target identity and rolls back on failure.

Expected status:

`LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED`

#### Build update package

Place desired target files under:

```text
<WorkspaceRoot>\AppCandidates\<ApplicationId>\
```

and add `.matawaka-target.json`, for example:

```json
{
  "Schema": "matawaka.local-app-target/v1",
  "ApplicationId": "demo.app",
  "TargetVersion": "1.1.0"
}
```

Then choose **Local apps → registered app → Build update package**.

Workbench reads current SHA-256 from the registered app, target bytes from the fixed candidate root, derives Add/Replace/NoOp, refuses implicit Delete, generates target identity, freshly recomputes both sides, writes one ZIP, validates that ZIP through the existing updater Preview, then persists a typed package-build receipt JSON.

Successful builder status:

`LOCAL_APPLICATION_UPDATE_PACKAGE_BUILT_EXISTING_UPDATER_PREVIEW_READY`

means only that the package is acceptable to the updater Preview; no update or launch occurred.

```text
Semantic Equality != Byte Equality
Explicit Action Label != Generic Dialog Button Semantics
Artifact Persistence != Receipt Persistence
Builder Preview != Package Write Authority
Package Write != Update Authority
Build Package != Update App != Launch App
```

To apply it, use **Local apps** again → **Update from package** and select the generated ZIP.

## Historical capabilities

Catalog fetch, JSON/agent analysis and recovery/evidence tools remain in source/history and can be re-surfaced later if real operator demand returns.

`UI simplification != Evidence erasure`.
