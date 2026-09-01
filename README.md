# Matawaka Workbench

Windows/.NET 10 WPF control plane for bounded local Matawaka analysis, authority/evidence inspection, self-hosted maintenance, recovery and explicit accepted-source publication.

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

The established semantic/runtime line remains separate from maintenance convenience: restricted Low-integrity SemanticHost, Windows Job Object, runtime attestation before semantic input, byte-bound host, read-only proposal path and denied Execute acceptance control.

## Current maintenance model

The normal maintenance path is intentionally human-readable while preserving typed internal gates:

```text
Update candidate
-> separate Launch candidate
-> separate Self-test
-> separate local Accept
-> separate Publish accepted
-> optional post-publication Lifecycle receipt
```

`Update candidate` sequences the existing typed package plan → staging materialization → staged apply plan → exact apply/build services. Their receipts remain individually preserved. Successful build still does not launch the candidate automatically.

```text
One operator session != One semantic authority
Successful Build != Candidate Launch
Candidate Launch != Self-test
Self-test PASS != Checkpoint Authority
Accepted Checkpoint != Publish Authority
Publication Success != Lifecycle Authority
```

## Maintenance Lifecycle Receipt

The lifecycle capability is evidence composition only. Starting with the qualification/stabilization adapter, it derives the current accepted version/predecessor from exact accepted Git/checkpoint evidence rather than release-specific constants.

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

The only lifecycle effect after a complete read-only assessment is an explicitly confirmed local evidence write under ignored `artifacts/lifecycle`.

## Fixed accepted-source publication

`Publish accepted` remains a separate human maintenance network gate with one destination only:

- remote name `github-workbench`;
- URL `https://github.com/Matawaka/workbench.git`;
- `refs/heads/main` plus the exact locally accepted tag.

Remote main must be exact local parent or already exact local HEAD. Conflicting main/tag fails closed. No force push, arbitrary remote, catalog mutation, Agent Execute, ActionPermit or general Workbench network authority is admitted.

## Qualification discipline

Patch-level qualification/stabilization is preferred over feature inflation when evidence reveals a recurring operational defect. A negative qualification result is valid.

Current lifecycle qualification outcomes are categorical:

- `LIFECYCLE_REUSABLE`;
- `LIFECYCLE_NEEDS_ADAPTER`;
- `LIFECYCLE_AMBIGUOUS`;
- `LIFECYCLE_NOT_REQUIRED`.

Workbench utility or successful maintenance does not establish canonical UU-AAP conformance, Stable Core membership, identity/trust, legal authority or general execution authority.
