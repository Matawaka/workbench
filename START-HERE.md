# Start here — Matawaka Workbench

This page describes the stable operator path. Exact release-specific predecessor/target identities are shown by update previews, the accepted tag at HEAD and `PATCH-v*.md` history rather than hard-coded here.

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

1. **Update Workbench** → choose the source-only ZIP and confirm the bounded maintenance session.
2. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
3. **Launch candidate** separately.
4. In the candidate run **Self-test** and require `Passed=true`.
5. **Accept** separately.
6. **Publish accepted** separately.
7. Optionally create **Lifecycle receipt** after publication and require `Complete=true`.

```text
Build != Launch
Launch != Self-test
Self-test PASS != Accept
Accept != Publish
Publish != Lifecycle authority
```

## Local apps

`Local apps` is contextual. Choose one direct child of:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

### Unregistered app

If `.matawaka-app.json` is absent, Workbench offers **Register**. Registration inventories the existing bytes, derives a deterministic `baseline-*` identity, freshly rechecks the bytes, then creates only `.matawaka-app.json`.

Expected status:

`LOCAL_APPLICATION_REGISTERED_UPDATE_AUTHORITY_NOT_CREATED`

### Registered app

If `.matawaka-app.json` exists, Workbench offers three possible local-app paths through the same top-level control:

- **Update from package**;
- **Build update package**;
- Cancel.

#### Update from package

Choose an existing `matawaka.local-app-update-package/v1` ZIP. Workbench validates exact current SHA-256 bindings and target payload digests, freshly revalidates, backs up predecessor bytes, applies Add/Replace only, verifies target identity and rolls back on failure.

Expected status:

`LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED`

#### Build update package

Place desired target files under:

```text
<WorkspaceRoot>\AppCandidates\<ApplicationId>\
```

and add:

```text
.matawaka-target.json
```

Example:

```json
{
  "Schema": "matawaka.local-app-target/v1",
  "ApplicationId": "demo.app",
  "TargetVersion": "1.1.0"
}
```

Then choose **Local apps → registered app → Build update package**.

Workbench:

1. reads current SHA-256 values directly from `Workspace\Apps\<ApplicationId>`;
2. reads target bytes from the fixed `Workspace\AppCandidates\<ApplicationId>` root;
3. derives Add / Replace / NoOp;
4. refuses any candidate omission that would imply Delete;
5. generates target `.matawaka-app.json` itself;
6. shows a read-only package plan;
7. after explicit confirmation freshly recomputes both sides;
8. writes one ZIP only under `Workbench\artifacts\local-app-packages`;
9. immediately validates that generated ZIP through the existing updater Preview.

Successful builder status means the package is already acceptable to the existing updater Preview, but no update has occurred.

```text
Semantic Equality != Byte Equality
Builder Preview != Package Write Authority
Package Write != Update Authority
Build Package != Update App != Launch App
```

To actually apply it, use **Local apps** again → **Update from package** and select the generated ZIP.

## Historical capabilities

Catalog fetch, JSON/agent analysis and recovery/evidence tools remain in source/history and can be re-surfaced later if real operator demand returns.

`UI simplification != Evidence erasure`.
