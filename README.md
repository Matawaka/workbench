# Matawaka Workbench

Windows/.NET 10 WPF control plane for bounded Matawaka maintenance, evidence inspection, accepted-source publication and managed local application maintenance.

## Accepted-state rule

The repository default branch `main` is the remotely published accepted source frontier. Development branches are not accepted merely because their source exists. The exact accepted version is the single `workbench-v<token>-accepted` tag at `main` HEAD; semantic runtime `Version` may be more specific than the tag/schema token.

`Accepted Source Documentation != Version-Specific Planning Document`

## Architecture

Workbench is a product/application implementation, not UU-AAP Stable Core:

- `Matawaka.Workbench.App` — WPF operator surface and explicit maintenance/evidence gates;
- `Matawaka.Workbench.Runtime` — command routing/runtime composition;
- `Matawaka.Workbench.Protocol` — Workbench-local typed contracts/progress semantics;
- `Matawaka.Workbench.AgentHost` — bounded development-agent host and Windows process/security boundary;
- `Matawaka.Workbench.Engine` — reusable analytic future adapter;
- `Matawaka.Workbench.Catalog` — local Matawaka catalog inspection;
- `Matawaka.Workbench.SemanticHost` — fixed verified semantic host.

Historical analysis/recovery/catalog controls remain source/evidence history, while the active product surface stays intentionally small.

## Active product surface

The normal window exposes exactly eight maintenance actions:

1. **Update Workbench**;
2. **Launch candidate**;
3. **Local apps**;
4. **Self-test**;
5. **Accept**;
6. **Publish accepted**;
7. **Lifecycle receipt**;
8. **Stop**.

There are no persistent Agent/git-fetch authority checkboxes in the active surface.

`Contextual Local Apps Action != Authority Collapse`

## Managed local applications

Current managed app bytes live only under:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

Desired candidate bytes for package building live only under:

```text
<WorkspaceRoot>\AppCandidates\<ApplicationId>\
```

The visible **Local apps** action remains one top-level control:

- unregistered app → **Register**;
- registered app → choose **Update from package** or **Build update package**.

### Registration

Registration accepts only an existing direct child of `Workspace\Apps`, inventories its bytes, computes a deterministic tree digest, and after separate confirmation creates only `.matawaka-app.json`. Its `baseline-*` version is an observed-byte baseline, not a vendor/upstream version claim.

```text
Register Local App != Import App
Register Local App != Update App
Register Local App != Launch App
```

### Update from package

The existing updater accepts only `matawaka.local-app-update-package/v1` ZIPs, derives the target root from `WorkspaceRoot + Apps + ApplicationId`, binds existing files by exact `CurrentSha256`, validates exact target payload digests, performs fresh revalidation, allows Add/Replace only, backs up predecessor bytes and rolls back on failure.

```text
Package Validity != Mutation Authority
Local App Update != App Launch
```

### Build update package

The builder removes the need to hand-author predecessor SHA-256 values.

A registered app is compared with the fixed candidate root `Workspace\AppCandidates\<ApplicationId>`. The candidate root contains desired target files plus:

```text
.matawaka-target.json
```

using schema `matawaka.local-app-target/v1` with exact `ApplicationId` and `TargetVersion`.

Builder Preview:

- reads current SHA-256 values directly from the registered app;
- reads target SHA-256 values from the fixed candidate root;
- derives Add / Replace / NoOp;
- refuses candidate omission that would imply Delete;
- generates target `.matawaka-app.json` bytes itself;
- synthesizes the exact updater manifest in memory;
- creates no package/update authority.

After separate confirmation the builder freshly revalidates both roots, writes one ZIP under `Workbench/artifacts/local-app-packages`, then immediately re-opens that ZIP through the existing updater `PreviewAsync`.

Builder success is reported only when:

```text
Existing Updater Preview == READY
```

This directly addresses byte-level package-authoring drift such as semantically equal JSON with different line endings.

```text
Semantic Equality != Byte Equality
Builder Preview != Package Write Authority
Package Write != Update Authority
Builder Success => Existing Updater Preview READY
Build Package != Update App != Launch App
```

The builder does not write under `Apps` or `AppCandidates`, perform the update, launch the app, use network/Git/installers, or create Agent Execute/ActionPermit.

See `LOCAL-APP-MAINTENANCE.md` for the stable registration/update/package-builder contract.

## Workbench maintenance model

```text
Update Workbench
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
-> optional Lifecycle receipt
```

Successful build never implies launch/accept/publication authority.

## Maintenance Lifecycle Receipt

Lifecycle V2 is evidence composition only. It derives accepted tag/schema token and semantic runtime Version separately, then binds the exact checkpoint, checkpoint-bound Self-test artifact, unique update-orchestrator receipt, fixed publication receipt and clean local source state. Missing or ambiguous evidence fails closed.

```text
Summary != Authority
Observed Sequence != Authorized Sequence
Tag/Schema Token != Semantic Runtime Version
Lifecycle Receipt != ActionPermit
```

## Fixed accepted-source publication

`Publish accepted` remains a separate human maintenance network gate with one destination only: `github-workbench` / `https://github.com/Matawaka/workbench.git`. Remote main must be exact local parent or already exact local HEAD. Conflicting main/tag fails closed; no force push or arbitrary remote is admitted.

## Qualification discipline

Real product capabilities receive executable fixture/use evidence where possible. Patch-level stabilization is preferred over feature inflation when evidence reveals a recurring defect. A negative qualification result is valid.

Workbench utility, successful local-app maintenance or source publication does not establish canonical UU-AAP conformance, Stable Core membership, identity/trust, legal authority or general execution authority.
