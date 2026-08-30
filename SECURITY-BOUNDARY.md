# Matawaka Workbench v0.13 — Security boundary

The accepted v0.7 semantic security boundary remains unchanged: fixed verified semantic host, restricted Low-integrity token, Windows Job Object and child runtime attestation before semantic input. Maintenance surfaces do not widen semantic-provider authority.

## Invariants

`Self-test PASS != Checkpoint authority`

`Valid package != Materialization authority`

`Staging materialization != Source apply/build authority`

`READY source plan != Source mutation authority`

`Apply/build authority != Candidate launch authority`

`Candidate launch != Acceptance`

`Workbench maintenance authority != Catalog mutation authority != Agent Execute`

## v0.13 materialization/planning

Materialization receipts now carry the accepted predecessor tag explicitly. Staged planning uses that tag and commit directly and no longer relies on a transition-specific hard-coded predecessor map.

The plan/materialization surfaces remain fail-closed on package SHA, predecessor, clean worktree, bounded staging root, exact file set and payload digest mismatches.

## v0.13 exact source apply + build

Before source mutation the gate requires:

1. an in-process staging-only materialization receipt;
2. a READY staged apply plan artifact;
3. a freshly regenerated equivalent plan;
4. unchanged accepted predecessor HEAD/tag;
5. a clean Workbench working tree;
6. exact current/staged hashes for every `Add`/`Replace` path;
7. explicit **Применить + собрать** confirmation.

Allowed source effect is limited to the exact plan paths. Replacement bytes are backed up under ignored `.workbench/update-source-backups`. A failure during apply/build restores the predecessor source and requires a new authorization attempt.

Allowed build process is limited to the fixed workspace-local `.dotnet-sdk\dotnet.exe` and fixed `build/publish --no-restore` arguments for the Workbench solution, App and SemanticHost. No executable path or arguments are taken from command JSON.

`--no-restore` and local cache roots mean the gate does not request package download. **OS network isolation is not enforced**, so this must not be described as a network sandbox.

The apply/build receipt does not permit Git add/commit/tag/fetch/push, remote publication, catalog mutation, Agent Execute, ActionPermit or checkpoint creation.

## Candidate launch

Launch has a separate confirmation. Only the exact receipt-bound candidate under Workbench artifacts may start, and its SHA-256 is reverified immediately before launch. Launch creates no acceptance/checkpoint authority and no authority over catalog repositories or Agent Execute.

The launched candidate must independently pass Self-test and then receive a separate explicit **Принять** confirmation for the fixed local Workbench Git checkpoint gate.
