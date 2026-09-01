# Start here — Matawaka Workbench

This page describes the stable operator path. Exact release-specific predecessor/target identities are shown by the update package preview, accepted tag at HEAD, and `PATCH-v*.md` history rather than hard-coded here.

## Active controls

The normal window intentionally exposes only:

- **Update Workbench**
- **Launch candidate**
- **Update local app**
- **Self-test**
- **Accept**
- **Publish accepted**
- **Lifecycle receipt**
- **Stop**

There are no persistent Agent or git-fetch checkboxes. Historical JSON/analysis/catalog/recovery controls remain in source/evidence history but are not active top-level product controls.

Workspace/Catalog path edits are saved on normal actions and window close; there is no separate Save button.

## Install/build a Workbench successor

1. Click **Update Workbench** and choose the source-only ZIP.
2. Review package SHA-256, exact predecessor commit/tag, target version/tag, payload count and bytes.
3. Explicitly confirm the maintenance session.
4. Workbench sequences the existing typed fresh plan → staging materialization → staged apply plan → exact apply/build gates.
5. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
6. Click **Launch candidate** separately and confirm the exact built executable SHA-256.

`Update Workbench != Launch authority`.

## Accept the launched Workbench

1. In the launched candidate click **Self-test**.
2. The Self-test click itself is the explicit human authority for the bounded read-only test matrix; a persistent Agent-enabled checkbox is not required.
3. Require `Passed=true`.
4. Click **Accept** and inspect exact changed files/build-source binding.
5. Explicitly create the local accepted checkpoint/tag.

```text
Self-test Click != Agent Execute
Self-test PASS != Checkpoint Authority
```

## Publish accepted source

Only after local acceptance:

1. click **Publish accepted**;
2. verify fixed `github-workbench` / `https://github.com/Matawaka/workbench.git`;
3. verify exact accepted HEAD, parent and accepted tag;
4. explicitly confirm publication;
5. require remote `main` and accepted tag to read back as exact local HEAD;
6. require local HEAD and working tree unchanged.

`Accepted checkpoint != Publish authority`.

## Create a Maintenance Lifecycle Receipt

Only after publication has independently completed:

1. click **Lifecycle receipt**;
2. Workbench derives the current accepted version from the unique accepted tag at HEAD;
3. it binds the exact checkpoint, checkpoint-bound Self-test, orchestrator and publication evidence;
4. require all exact relations and artifact SHA-256s to pass;
5. explicitly confirm the local lifecycle evidence write.

Missing/ambiguous evidence fails closed.

`Summary != Authority`.

## Update another local application

A managed local app must already exist at:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

and contain `.matawaka-app.json` with schema `matawaka.local-app-identity/v1`, matching app id and current version.

To update it:

1. click **Update local app**;
2. choose a local ZIP using `matawaka.local-app-update-package/v1`;
3. review ApplicationId, fixed derived root, current → target version, package/manifest SHA-256 and exact Add/Replace paths;
4. confirm the update;
5. Workbench freshly revalidates the package/app state, backs up replacement bytes and applies only exact manifest payload bytes;
6. require status `LOCAL_APPLICATION_UPDATED_SEPARATE_LAUNCH_REQUIRED`;
7. inspect the Local Apps receipt tab;
8. launch the updated application manually only if you want to.

The updater does not download anything and cannot run MSI/EXE/scripts, delete files, mutate registry/services, use Git, launch the app automatically or target a path outside `Workspace\Apps\<ApplicationId>`.

```text
Package Validity != Mutation Authority
Local App Update != App Launch
Managed Root != Arbitrary Target Root
```

Initial registration/adoption of an existing application is intentionally separate from v0.35 update authority.

## Historical capabilities

Catalog fetch, JSON/agent analysis and recovery/evidence tools remain in source/history and can be re-surfaced later if real operator demand returns.

`UI simplification != Evidence erasure`.
