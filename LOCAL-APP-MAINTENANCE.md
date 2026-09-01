# Local Application Maintenance

This document defines the stable Workbench contract for registering and updating managed local applications. It is deliberately **not** a general installer, importer, launcher or filesystem manager.

## Managed root

Only direct children of:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

are eligible.

`ApplicationId` must match:

```text
[A-Za-z0-9][A-Za-z0-9._-]{0,63}
```

Workbench does not register an arbitrary outside directory in place and does not copy/import a selected external app into the managed root.

```text
Managed Root != Arbitrary Target Root
Register Local App != Import App
```

## Registration

Registration exists only for an application directory that is already an immediate child of `Workspace\Apps` and does not yet contain `.matawaka-app.json`.

### Read-only preview

Workbench:

- derives ApplicationId from the exact folder name;
- rejects reparse points at the Apps root, app root, subdirectories or files;
- inventories regular app files recursively;
- bounds the inventory to 4096 files and 2 GiB total bytes;
- records normalized relative path, SHA-256 and file size for every file;
- computes a deterministic tree SHA-256 over ordered `(path,sha256,size)` tuples;
- proposes an identity:

```json
{
  "Schema": "matawaka.local-app-identity/v1",
  "ApplicationId": "<folder-name>",
  "Version": "baseline-<first16-of-tree-sha256>"
}
```

The `baseline-*` value is a deterministic Workbench evidence marker for the currently observed bytes. It is **not** a vendor/upstream version claim.

### Registration effect

Only after explicit confirmation:

1. Workbench repeats the full inventory and requires it to equal the preview;
2. `.matawaka-app.json` must still be absent;
3. Workbench atomically creates exactly that identity sidecar;
4. Workbench re-reads/verifies identity bytes and SHA-256;
5. Workbench re-inventories all pre-existing app files and requires the original tree digest unchanged;
6. Workbench writes one local registration receipt under ignored Workbench artifacts.

If registration fails after identity creation, Workbench removes the identity and verifies the original application byte baseline again.

Success status:

`LOCAL_APPLICATION_REGISTERED_UPDATE_AUTHORITY_NOT_CREATED`

Registration does not authorize update or launch.

```text
Preview PASS != Registration Authority
Registration != Update Authority
Registration != Launch Authority
Identity Creation != Vendor Identity Assertion
```

## Existing application identity

A registered managed app contains `.matawaka-app.json` with schema `matawaka.local-app-identity/v1`, exact ApplicationId and current maintenance version/baseline.

That file is local maintenance evidence. It is not proof of real-world producer identity, trust or legal authority.

## Update ZIP

Registered apps may be updated from a local ZIP with schema:

`matawaka.local-app-update-package/v1`

Exact shape:

```text
local-app-update-manifest.json
payload/.matawaka-app.json
payload/<other manifest-declared files>
```

No undeclared ZIP entry is accepted.

The manifest includes ApplicationId, ExpectedCurrentVersion, TargetVersion, exact file list, predecessor `CurrentSha256` for every Replace, target `Sha256` for every payload file, and all requested network/process/installer/registry/service/environment/AgentExecute flags set to false.

The target identity payload must use the same ApplicationId and TargetVersion.

## Update path/file rules

Every manifest path is relative to the fixed application root. Rejected:

- absolute/rooted paths or drive prefixes;
- `.` / `..` segments or traversal;
- duplicate/Windows case-colliding paths;
- existing directory where a file is expected;
- reparse-point escape.

Existing destination files require exact `CurrentSha256` and become `Replace`; missing destinations require no predecessor digest and become `Add`. Delete is unsupported.

## Update effect boundary

The bounded updater first performs a read-only Preview. Only after explicit confirmation it reruns Preview and requires an equivalent plan.

Before mutation it backs up exact Replace bytes. Files are written through temporary paths and SHA-256 verified; `.matawaka-app.json` is applied last. Target digests/identity are reverified afterward.

On failure:

- new Add files are removed;
- Replace files are restored from exact backups;
- predecessor digests/identity are reverified.

Success status:

`LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED`

## Authority ceiling

Neither registration nor update authorizes or performs:

- app import/copy/move from an arbitrary outside root;
- package download/network access;
- Git operations;
- app/process launch;
- MSI/EXE/script installer execution;
- Windows registry/service/environment mutation;
- arbitrary target roots;
- Workbench source mutation;
- Matawaka catalog mutation;
- Agent Execute / ActionPermit;
- canonical UU-AAP conformance or Stable Core promotion.

```text
Register Local App != Import App
Local App Update != App Launch
Package Validity != Mutation Authority
Explicit Confirmation != General Filesystem Authority
```
