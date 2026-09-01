# Matawaka Workbench

Windows/.NET 10 WPF control plane for bounded Matawaka maintenance, evidence inspection, accepted-source publication and local application maintenance.

## Accepted-state rule

The repository default branch `main` is the remotely published accepted source frontier. Development/stabilization branches are not accepted merely because their source exists.

The exact accepted version is identified by the single `workbench-v<version>-accepted` tag at `main` HEAD. Version-specific predecessor SHAs, development plans and acceptance notes belong in `PATCH-v*.md`, Git tags and issue history rather than in this permanent README.

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

The normal window exposes only eight maintenance actions:

1. **Update Workbench** — one bounded package → materialize → apply-plan → apply/build session;
2. **Launch candidate** — separate exact receipt-bound Workbench candidate launch;
3. **Update local app** — bounded update of a registered app under `Workspace\Apps\<ApplicationId>`;
4. **Self-test** — explicit bounded read-only acceptance matrix;
5. **Accept** — separate local Workbench commit/tag;
6. **Publish accepted** — separate fixed GitHub fast-forward/tag publication;
7. **Lifecycle receipt** — post-publication evidence composition only;
8. **Stop** — cancel the current bounded run.

There are no persistent `Agent enabled` or `Allow git fetch` checkboxes in the active product surface. The Self-test click itself is the explicit human authority to run the bounded test matrix. Historical JSON/agent/catalog/recovery controls remain hidden compatibility/source surfaces and are not erased.

```text
Hidden Control != Deleted Capability
Hidden Control != Lost Evidence
Persistent Checkbox Removed != Authority Made Implicit
Self-test Click != Agent Execute
```

## Workbench maintenance model

```text
Update Workbench
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
-> optional post-publication Lifecycle receipt
```

`Update Workbench` sequences the existing typed package plan → staging materialization → staged apply plan → exact apply/build services. Their receipts remain individually preserved. Successful build still does not launch the candidate automatically.

```text
One operator session != One semantic authority
Successful Build != Candidate Launch
Candidate Launch != Self-test
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Publish Authority
Publication Success != Lifecycle Authority
```

## Local application maintenance

Workbench can update other registered local applications without granting a general installer or filesystem capability.

Managed applications live only under:

```text
<WorkspaceRoot>\Apps\<ApplicationId>\
```

Each application must already contain:

```text
.matawaka-app.json
```

with schema `matawaka.local-app-identity/v1`, the exact `ApplicationId`, and current version.

`Update local app` consumes a local ZIP with schema `matawaka.local-app-update-package/v1`. The package contains one manifest plus exact `payload/` files. Every replacement file is bound to both predecessor and target SHA-256; Add paths have no predecessor digest. The target identity file must also be included and must bind the same app id and target version.

The updater:

- derives the target root only from `WorkspaceRoot + Apps + ApplicationId`;
- refuses traversal/rooted paths and reparse-point escape;
- validates the exact ZIP entry set and every payload SHA-256;
- validates current app identity/version and replacement digests;
- creates a fresh preview before mutation;
- backs up exact replaced bytes;
- allows Add/Replace only, never Delete;
- writes via temporary files and re-verifies target digests/identity;
- rolls back predecessor bytes if apply fails;
- writes a bounded local update receipt.

It does **not** download packages, run installers/scripts, launch the updated app, mutate Git, Windows registry/services/environment, catalog repositories, or create Agent Execute/ActionPermit authority.

```text
Local App Update != App Launch
Package Validity != Mutation Authority
Managed Root != Arbitrary Target Root
Explicit Update App Confirmation != General Filesystem Authority
```

See `LOCAL-APP-MAINTENANCE.md` for the identity/package contract.

## Maintenance Lifecycle Receipt

The lifecycle capability is evidence composition only. It derives the current accepted version/predecessor from exact accepted Git/checkpoint evidence rather than release-specific constants.

A complete assessment requires one exact relation among:

- the current accepted tag at HEAD;
- the exact local checkpoint for that HEAD/tag;
- the checkpoint-bound passing Self-test artifact + SHA-256;
- the unique update orchestrator receipt targeting that accepted version/predecessor and candidate executable digest;
- the unique fixed publication receipt whose local/remote main/tag equal the accepted commit;
- clean current Workbench source state.

Missing or ambiguous evidence fails closed. Artifact selection is never based on modification time.

```text
Summary != Authority
Observed Sequence != Authorized Sequence
Accepted Tag Discovery != Trust Discovery
Artifact Path != Artifact Identity
Latest File != Correct File
Lifecycle Receipt != ActionPermit
```

## Fixed accepted-source publication

`Publish accepted` remains a separate human maintenance network gate with one destination only:

- remote name `github-workbench`;
- URL `https://github.com/Matawaka/workbench.git`;
- `refs/heads/main` plus the exact locally accepted tag.

Remote main must be exact local parent or already exact local HEAD. Conflicting main/tag fails closed. No force push, arbitrary remote, catalog mutation, Agent Execute, ActionPermit or general Workbench network authority is admitted.

## Qualification discipline

Patch-level qualification/stabilization is preferred over feature inflation when evidence reveals a recurring operational defect. A negative qualification result is valid.

Workbench utility, successful local-app maintenance or successful source publication does not establish canonical UU-AAP conformance, Stable Core membership, identity/trust, legal authority or general execution authority.
