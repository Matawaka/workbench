# Matawaka Workbench

Windows/.NET 10 WPF control plane for bounded Matawaka maintenance, evidence inspection, accepted-source publication and managed local application maintenance.

## Accepted-state rule

The repository default branch `main` is the remotely published accepted source frontier. Development branches are not accepted merely because their source exists. The exact accepted version is the single `workbench-v<token>-accepted` tag at `main` HEAD; semantic runtime `Version` may be more specific than the tag/schema token (for example `0.35.0` under token `0.35`).

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

Historical analysis/recovery/catalog controls remain source/evidence history, but the active product surface is intentionally smaller.

## Active product surface

The normal window exposes exactly eight maintenance actions:

1. **Update Workbench** — bounded package → materialize → apply-plan → apply/build session;
2. **Launch candidate** — separate exact receipt-bound Workbench launch;
3. **Local apps** — contextual registration or bounded update of a managed local application;
4. **Self-test** — explicit bounded read-only acceptance matrix;
5. **Accept** — separate local Workbench commit/tag;
6. **Publish accepted** — separate fixed GitHub fast-forward/tag publication;
7. **Lifecycle receipt** — post-publication evidence composition only;
8. **Stop** — cancel the current bounded run.

There are no persistent `Agent enabled` or `Allow git fetch` checkboxes in the active product surface. Historical JSON/agent/catalog/recovery controls remain hidden compatibility/source surfaces and are not erased.

```text
Hidden Control != Deleted Capability
Persistent Checkbox Removed != Authority Made Implicit
Self-test Click != Agent Execute
Contextual Local Apps Action != Authority Collapse
```

## Workbench maintenance model

```text
Update Workbench
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
-> optional Lifecycle receipt
```

`Update Workbench` sequences existing typed package intake, staging materialization, staged apply plan and exact apply/build gates. Successful build still does not launch the candidate automatically.

## Managed local applications

Managed applications live only under:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

The visible **Local apps** action first asks the operator to choose one direct child directory of `Workspace\Apps`.

### Register an existing managed-directory app

If `.matawaka-app.json` is absent, Workbench offers a separate registration preview.

Registration:

- derives `ApplicationId` from the direct-child folder name;
- refuses arbitrary/outside roots and reparse-point boundaries;
- inventories up to 4096 existing regular files / 2 GiB total;
- binds exact normalized relative paths, file sizes and SHA-256s;
- computes a deterministic tree SHA-256;
- proposes identity schema `matawaka.local-app-identity/v1` with `Version = baseline-<first16-tree-digest>`;
- freshly re-runs the complete inventory after confirmation;
- creates **only** `.matawaka-app.json` atomically;
- verifies all pre-existing application bytes remain unchanged;
- writes a Workbench-local registration receipt.

The `baseline-*` value is an observed-byte baseline, not a claim about a vendor/upstream product version.

```text
Register Local App != Import App
Register Local App != Update App
Register Local App != Launch App
Registration Baseline != Vendor Version Claim
Identity Creation != General Filesystem Authority
```

Workbench does not copy/move an external app into `Workspace\Apps`; the operator intentionally places an application directory there before registration.

### Update a registered app

If `.matawaka-app.json` exists, **Local apps** asks for a local ZIP using schema `matawaka.local-app-update-package/v1` and delegates to the bounded updater.

The updater derives the target root only from `WorkspaceRoot + Apps + ApplicationId`, validates the exact ZIP entry set/current and target SHA-256s, performs a fresh preview, backs up replacement bytes, allows Add/Replace only, applies the identity last, verifies the target state, and rolls back predecessor bytes on failure.

It does **not** download packages, run installers/scripts, launch the updated app, mutate Git/registry/services/environment/catalog, or create Agent Execute/ActionPermit authority.

```text
Package Validity != Mutation Authority
Local App Update != App Launch
Managed Root != Arbitrary Target Root
```

See `LOCAL-APP-MAINTENANCE.md` for the registration/update contract.

## Maintenance Lifecycle Receipt

Lifecycle V2 is evidence composition only. It derives accepted tag/schema token and semantic runtime Version separately, then binds the exact checkpoint, checkpoint-bound Self-test artifact, unique update-orchestrator receipt, fixed publication receipt and clean local source state. Missing or ambiguous evidence fails closed.

```text
Summary != Authority
Observed Sequence != Authorized Sequence
Accepted Tag Discovery != Trust Discovery
Tag/Schema Token != Semantic Runtime Version
Lifecycle Receipt != ActionPermit
```

## Fixed accepted-source publication

`Publish accepted` remains a separate human maintenance network gate with one destination only:

- remote name `github-workbench`;
- URL `https://github.com/Matawaka/workbench.git`;
- `refs/heads/main` plus the exact locally accepted tag.

Remote main must be exact local parent or already exact local HEAD. Conflicting main/tag fails closed. No force push, arbitrary remote, local-app authority, catalog mutation, Agent Execute, ActionPermit or general Workbench network authority is admitted.

## Qualification discipline

Real product capabilities receive real fixture/use evidence where possible. Patch-level stabilization is preferred over feature inflation when evidence reveals a recurring defect. A negative qualification result is valid.

Workbench utility, successful local-app maintenance or successful source publication does not establish canonical UU-AAP conformance, Stable Core membership, identity/trust, legal authority or general execution authority.
