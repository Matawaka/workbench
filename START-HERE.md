# Start here — Matawaka Workbench

This page describes the stable operator path. Exact release-specific predecessor/target identities are shown by the update package preview, accepted tag at HEAD, and `PATCH-v*.md` history rather than hard-coded here.

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

There are no persistent Agent or git-fetch checkboxes. Historical JSON/analysis/catalog/recovery controls remain source/evidence history but are not active top-level product controls.

Workspace/Catalog path edits are saved on normal actions and window close; there is no separate Save button.

## Install/build a Workbench successor

1. Click **Update Workbench** and choose the source-only ZIP.
2. Review package SHA-256, exact predecessor commit/tag, target version/tag, payload count and bytes.
3. Explicitly confirm the maintenance session.
4. Workbench sequences fresh plan → staging materialization → staged apply plan → exact apply/build.
5. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
6. Click **Launch candidate** separately and confirm the exact built executable SHA-256.

`Update Workbench != Launch authority`.

## Accept / publish / lifecycle

1. In the launched candidate click **Self-test** and require `Passed=true`.
2. Click **Accept** and explicitly create the local accepted checkpoint/tag.
3. Click **Publish accepted** separately; require fixed remote main/tag exact readback and unchanged local state.
4. Click **Lifecycle receipt** separately; require exact checkpoint/Self-test/orchestrator/publication bindings and `Complete=true`.

```text
Self-test Click != Agent Execute
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Publish Authority
Publication Success != Lifecycle Authority
Summary != Authority
```

## Local apps — register or update

`Local apps` is contextual. First choose one **direct child** of:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

Workbench does not import/copy a folder from elsewhere. Put the application directory under `Workspace\Apps` intentionally first.

### If `.matawaka-app.json` is absent

Workbench offers **Register local app**:

1. it derives ApplicationId from the selected folder name;
2. performs a read-only SHA-256 inventory of the existing app bytes;
3. shows file count/bytes/tree SHA-256 and proposed `baseline-<digest>` identity;
4. after explicit confirmation it freshly repeats the inventory;
5. it creates only `.matawaka-app.json`;
6. it re-verifies all pre-existing app files stayed unchanged;
7. it writes a registration receipt.

Expected status:

`LOCAL_APPLICATION_REGISTERED_UPDATE_AUTHORITY_NOT_CREATED`

The baseline is an observed-byte marker, not a vendor version claim.

```text
Register != Import
Register != Update
Register != Launch
Identity Creation != Vendor Identity Assertion
```

### If `.matawaka-app.json` already exists

Workbench asks for a local `matawaka.local-app-update-package/v1` ZIP and performs the existing bounded update flow:

1. preview exact ApplicationId/root/current→target version/package SHA-256/Add/Replace paths;
2. explicit confirmation;
3. fresh preview revalidation;
4. predecessor backup for Replace files;
5. exact Add/Replace apply, target identity last;
6. exact digest verification or rollback;
7. status `LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED`.

No app is launched automatically.

## Historical capabilities

Catalog fetch, JSON/agent analysis and recovery/evidence tools remain in source/history and can be re-surfaced later if real operator demand returns.

`UI simplification != Evidence erasure`.
