# Matawaka Workbench v0.35 — Product Surface & Local Application Maintenance

Status: feature candidate over exact accepted/published v0.34.1. This is a product capability increment, not UU-AAP Stable Core promotion or canonical conformance.

## Exact predecessor

- commit: `c69d3237bae06b80481ce2421eb34e8cf1a88c1b`
- tag: `workbench-v0.34.1-accepted`
- parent: `224ad00bd72b0534de1081b2a20c44746ee0e7a0`
- predecessor lifecycle qualification: `LIFECYCLE_REUSABLE`

## Why this increment exists

The operator requested two concrete product changes:

1. keep only controls actually used in the current Workbench maintenance workflow;
2. add an important capability for updating other local applications.

This is direct product demand, so a feature successor is justified after the qualification/stabilization pause.

## Active surface reduction

Visible primary actions become exactly:

1. `Update Workbench`
2. `Launch candidate`
3. `Update local app`
4. `Self-test`
5. `Accept`
6. `Publish accepted`
7. `Lifecycle receipt`
8. `Stop`

No persistent visible checkboxes remain.

Removed from active UI only:

- Paste JSON / File / Validate / Run;
- Recovery check / plan / execute;
- Catalog scan / fetch;
- persistent Agent-enabled checkbox;
- persistent Allow-git-fetch checkbox;
- explicit Save button.

Their source/history is retained through collapsed compatibility bindings and Git history.

`Hidden Control != Deleted Capability != Evidence Erasure`

### Self-test authority after checkbox removal

The explicit Self-test click is itself the human decision to run the bounded read-only acceptance matrix. v0.35 passes `AgentEnabled=true` only inside that bounded Self-test context; this does not expose general Agent Execute.

`Self-test Click != Agent Execute`

## Local Application Maintenance

New service:

`LocalApplicationMaintenanceService`

New active button:

`Update local app`

### Fixed managed root

Only:

`<WorkspaceRoot>/Apps/<ApplicationId>/`

The package cannot provide an arbitrary target root.

A managed application must already contain `.matawaka-app.json`:

```json
{
  "Schema": "matawaka.local-app-identity/v1",
  "ApplicationId": "example.app",
  "Version": "1.0.0"
}
```

Initial registration/adoption is intentionally not part of update authority.

### Package contract

Schema:

`matawaka.local-app-update-package/v1`

ZIP:

```text
local-app-update-manifest.json
payload/.matawaka-app.json
payload/<exact declared files>
```

Manifest binds:

- ApplicationId;
- ExpectedCurrentVersion;
- TargetVersion;
- exact file paths;
- CurrentSha256 for replacement paths;
- target Sha256 for every payload path;
- effect request flags, all required false.

No Delete action exists in v0.35.

### Read-only preview

Before confirmation the service validates:

- bounded package size/file count;
- exact ZIP entry set;
- path safety / duplicate Windows-case collisions;
- fixed app root;
- no reparse-point root/parent escape;
- exact current app identity/version;
- exact current replacement digests;
- exact target payload digests;
- exact target identity app/version;
- no requested network/process/installer/registry/service/environment/AgentExecute effect.

Preview status only makes a later explicit Update local app confirmation eligible.

### Fresh apply

After explicit confirmation:

1. rerun Preview;
2. require an equivalent package/app/file relation;
3. backup exact replacement bytes under ignored Workbench storage;
4. apply Add/Replace exact bytes only under fixed app root;
5. apply `.matawaka-app.json` after ordinary payload files;
6. verify all target file digests and target identity/version;
7. write local update receipt.

Success status:

`LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED`

The app is not launched automatically.

### Rollback

On failure after backup:

- remove Add files;
- restore Replace files from exact backups;
- verify predecessor digests and identity bytes/version;
- report bounded rollback result.

## Local-app authority ceiling

```text
Package Validity != Mutation Authority
Local App Update != App Launch
Managed Root != Arbitrary Target Root
Explicit Update Confirmation != General Filesystem Authority
Initial Registration != Update Authority
```

No network/download, Git, installer/script execution, app launch, Delete, registry/service/environment mutation, Workbench source mutation, catalog mutation, Agent Execute, ActionPermit, canonical UU-AAP conformance or Stable Core promotion.

## v0.35 acceptance successor

- Self-test `0.35.0` preserves the accepted v0.34.1 matrix and adds offline local-app contract checks;
- exact predecessor is accepted v0.34.1;
- local target tag `workbench-v0.35-accepted`;
- fixed GitHub publisher keeps exact non-force fast-forward/tag contract;
- successor-generic Lifecycle receipt stays separate.

## Candidate acceptance sequence

1. accepted v0.34.1: **Update Workbench** with source-only v0.35 ZIP;
2. require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`;
3. separately **Launch candidate**;
4. v0.35 candidate: **Self-test** → require `Passed=true`;
5. separately **Accept** → local `workbench-v0.35-accepted`;
6. separately **Publish accepted**;
7. separately **Lifecycle receipt** → require `Complete=true`;
8. independently verify remote main/tag and accepted product bytes.

A real local application update is a later product qualification, not a requirement for accepting the Workbench implementation itself.

## Next evidence gate

After v0.35 acceptance, use a real already-registered local app package before expanding this into registration, update-feed discovery, arbitrary installers or auto-launch.
