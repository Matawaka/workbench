# Local App Chat Handoff

This document defines a conservative operator handoff convention for receiving a local application from another ChatGPT conversation and then using the already accepted Workbench Local Apps flows.

It is **not** a new Workbench import protocol and creates no filesystem copy/move authority. The operator still extracts the archive explicitly and then uses the existing `Local apps` action.

## 1. New application seed capsule

Filename:

```text
matawaka-local-app-seed-<applicationId>-<version>.zip
```

The ZIP MUST contain exactly one top-level directory:

```text
<applicationId>/
```

Inside that directory are only the ordinary application files that should become the initial managed application bytes.

The seed MUST NOT contain:

```text
.matawaka-app.json
.matawaka-target.json
```

Reason:

- `.matawaka-app.json` is Workbench-managed identity and must be created by `Local apps -> Register` from the actual local bytes;
- `.matawaka-target.json` belongs to the candidate role under `AppCandidates`, not to the managed `Apps` role.

The producing chat should provide, separately from the ZIP:

- exact `ApplicationId`;
- human version label;
- ZIP SHA-256;
- short file inventory;
- startup instructions, if any;
- required external dependencies, if any.

Operator path:

```text
1. Verify the supplied ZIP SHA-256.
2. Extract the single <applicationId>/ directory under <WorkspaceRoot>/Apps/.
3. Open Workbench -> Local apps.
4. Select <WorkspaceRoot>/Apps/<applicationId>.
5. Use Register.
6. Preserve the resulting Workbench registration receipt.
```

Registration creates identity from the actual local bytes. Receipt/evidence does not imply update or launch authority.

## 2. Update candidate capsule

Filename:

```text
matawaka-local-app-candidate-<applicationId>-<targetVersion>.zip
```

The ZIP MUST contain exactly one top-level directory:

```text
<applicationId>/
```

It contains the desired complete ordinary target file set plus exactly one target-role metadata file:

```text
.matawaka-target.json
```

Required metadata shape:

```json
{
  "Schema": "matawaka.local-app-target/v1",
  "ApplicationId": "<applicationId>",
  "TargetVersion": "<targetVersion>"
}
```

The candidate MUST NOT contain `.matawaka-app.json`; Workbench synthesizes the target identity itself.

Operator path:

```text
1. Verify the supplied ZIP SHA-256.
2. Extract the single <applicationId>/ directory under <WorkspaceRoot>/AppCandidates/.
3. Open Workbench -> Local apps.
4. Select the already registered <WorkspaceRoot>/Apps/<applicationId>.
5. Choose Build update package.
6. Review Add / Replace / NoOp and any refused implicit Delete.
7. Confirm Build only if the preview is correct.
8. Preserve the package-build receipt.
9. Apply the generated package later through the separate Update from package action if desired.
```

## 3. Required role separation

```text
Apps/<ApplicationId>          = managed application bytes
AppCandidates/<ApplicationId> = desired target candidate bytes
```

Do not place `.matawaka-target.json` under the managed `Apps` root. The accepted v0.37.1 guard intentionally refuses that role collision.

```text
Candidate Source != Managed Application
Seed Handoff != Registration
Candidate Handoff != Update Package
Build Package != Update App != Launch App
```

## 4. What to request from another ChatGPT conversation

For a brand-new local program, ask for:

```text
Create the finished local application as a ZIP named
matawaka-local-app-seed-<applicationId>-<version>.zip.

The ZIP must contain exactly one top-level <applicationId>/ folder with the ordinary application files only.
Do not include .matawaka-app.json or .matawaka-target.json.
Also give me the ZIP SHA-256, a file inventory, startup instructions, and external dependency requirements.
Do not invent Workbench registration identity; Workbench will create it from the local bytes after extraction.
```

For a later update, request the candidate form instead and require `.matawaka-target.json` with schema `matawaka.local-app-target/v1`.

## 5. Current limitation

Workbench v0.41 does not add one-click ZIP import/extraction. Manual extraction is deliberate: adding bounded import/copy authority is a separate future capability and should be justified and qualified independently.
