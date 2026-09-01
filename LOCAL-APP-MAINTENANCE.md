# Local Application Maintenance

This document defines the stable Workbench contract for bounded updates of other local applications.

## Scope

The updater is deliberately **not** a general installer.

Managed application root:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

The update package never supplies a target root. Workbench derives it from the current Workspace root plus the manifest ApplicationId.

`Managed Root != Arbitrary Target Root`

## Existing application identity

A managed application must already exist and contain:

`.matawaka-app.json`

Example:

```json
{
  "Schema": "matawaka.local-app-identity/v1",
  "ApplicationId": "kontur.desktop",
  "Version": "1.2.0"
}
```

ApplicationId must match:

```text
[A-Za-z0-9][A-Za-z0-9._-]{0,63}
```

The identity file is evidence about the managed app/version for this local maintenance protocol. It is not proof of real-world producer identity, trust or authority.

Initial adoption/registration of an arbitrary existing folder is intentionally outside the v0.35 updater. A future registration capability must receive its own boundary because choosing a root is materially broader authority than updating an already-identified managed root.

## Update ZIP

Schema:

`matawaka.local-app-update-package/v1`

Exact ZIP shape:

```text
local-app-update-manifest.json
payload/.matawaka-app.json
payload/<other manifest-declared files>
```

No undeclared ZIP entry is accepted.

Example manifest:

```json
{
  "Schema": "matawaka.local-app-update-package/v1",
  "PackageVersion": "1",
  "ApplicationId": "kontur.desktop",
  "ExpectedCurrentVersion": "1.2.0",
  "TargetVersion": "1.3.0",
  "PayloadRoot": "payload/",
  "Files": [
    {
      "Path": "app/Kontur.exe",
      "CurrentSha256": "<64-hex-current-file-digest>",
      "Sha256": "<64-hex-target-file-digest>"
    },
    {
      "Path": "assets/new-policy.json",
      "CurrentSha256": null,
      "Sha256": "<64-hex-target-file-digest>"
    },
    {
      "Path": ".matawaka-app.json",
      "CurrentSha256": "<64-hex-current-identity-digest>",
      "Sha256": "<64-hex-target-identity-digest>"
    }
  ],
  "NetworkAccessRequested": false,
  "ProcessLaunchRequested": false,
  "InstallerScriptExecutionRequested": false,
  "RegistryMutationRequested": false,
  "ServiceMutationRequested": false,
  "EnvironmentMutationRequested": false,
  "AgentExecuteRequested": false,
  "NonEffects": [
    "no network",
    "no installer execution",
    "no automatic app launch"
  ]
}
```

The target `payload/.matawaka-app.json` must use the identity schema, same ApplicationId and TargetVersion.

## Path rules

Every manifest path is relative to the fixed application root.

Rejected:

- absolute/rooted paths;
- drive prefixes / `:`;
- `.` or `..` path segments;
- NUL characters;
- duplicate or Windows case-colliding paths;
- existing directory where a manifest file is expected;
- application root or existing parent path segments that are reparse points.

These checks prevent a package from converting app-update authority into arbitrary filesystem authority.

## File-state rules

For an existing destination file:

- `CurrentSha256` is required;
- actual current digest must equal it;
- update action is `Replace`.

For a missing destination file:

- `CurrentSha256` must be null/empty;
- update action is `Add`.

`Delete` is not supported.

Every target payload SHA-256 must match before the preview is READY.

## Human/effect boundary

`Update local app` first performs a read-only Preview.

Preview validates:

- ZIP/package structure;
- exact entry set;
- current app identity/version;
- current replacement digests;
- target payload digests;
- target identity;
- managed-root/reparse boundary;
- requested-effect flags.

Only after the user sees and confirms the exact app/version/package/files does Workbench obtain one bounded Add/Replace authority.

Immediately before mutation it reruns Preview and requires an equivalent plan.

```text
Preview PASS != Mutation Authority
Old Preview != Fresh Apply Evidence
Explicit Confirmation != General Filesystem Authority
```

## Apply / rollback

Before mutation, exact predecessor bytes for every Replace path are copied to Workbench-local ignored backup storage.

Each target file is written to a temporary file, SHA-256 verified, then moved into the destination.

The identity file is applied after ordinary payload files so a partially completed update does not advertise the target version early.

After apply Workbench re-verifies all target file digests and target identity/version.

If apply fails after backup:

- new Add files are removed;
- Replace files are restored from exact backups;
- predecessor file digests and predecessor identity bytes/version are re-verified;
- failure is reported only after bounded rollback verification.

## Receipt

Success status:

`LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED`

The receipt binds at minimum:

- ApplicationId/root;
- previous/target version;
- package SHA-256;
- manifest SHA-256;
- previous/current identity SHA-256;
- exact Add/Replace path/digests;
- backup root;
- fresh-preview verification;
- target identity verification;
- `AppLaunchPerformed=false`;
- authority and non-effects.

## Authority ceiling

The local-app updater does not authorize or perform:

- package download/network access;
- Git operations;
- app/process launch;
- MSI/EXE/script installer execution;
- deletion of existing paths;
- Windows registry/service/environment mutation;
- arbitrary target roots;
- Workbench source mutation;
- Matawaka catalog mutation;
- Agent Execute / ActionPermit;
- canonical UU-AAP conformance or Stable Core promotion.

`Local App Update != App Launch`
