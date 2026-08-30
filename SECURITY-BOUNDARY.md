# Matawaka Workbench v0.14 — Security boundary

The accepted v0.7 semantic security boundary remains unchanged: fixed verified semantic host, restricted Low-integrity token, Windows Job Object and child runtime attestation before semantic input. Maintenance surfaces do not widen semantic-provider authority.

## Invariants

`Self-test PASS != Checkpoint authority`

`Valid package != Materialization authority`

`Staging materialization != Source apply/build authority`

`READY source plan != Source mutation authority`

`Apply/build authority != Candidate launch authority`

`Candidate launch != Acceptance`

`Workbench maintenance authority != Catalog mutation authority != Agent Execute`

## Self-hosted update loop

v0.14 does not add a broader effect surface. Its purpose is to exercise the already-separated package-plan, staging materialization, staged planning, exact apply/build, launch, Self-test and local checkpoint gates as one successor transition while preserving a distinct receipt and explicit authority decision at each effectful boundary.

A receipt from an earlier gate cannot be reused as authorization for a later gate. Every effectful step revalidates the byte-bound predecessor/evidence it consumes.

## Exact source apply + build

Before source mutation the gate requires an in-process staging materialization, a READY staged plan artifact, a freshly regenerated equivalent plan, unchanged accepted predecessor HEAD/tag, a clean Workbench working tree, exact current/staged hashes for every changed path, and explicit **Применить + собрать** confirmation.

Allowed source effect is limited to the exact planned paths. Replacement bytes are backed up under ignored `.workbench/update-source-backups`. A failure during apply/build restores predecessor source and requires a new authorization attempt.

Allowed build process is limited to the fixed workspace-local `.dotnet-sdk\dotnet.exe` and fixed `build/publish --no-restore` arguments for the Workbench solution, App and SemanticHost. No executable path or arguments are taken from command JSON.

`--no-restore` means this gate requests no package restore. **OS network isolation is not enforced**, so this must not be described as a network sandbox.

## Candidate launch and checkpoint

Launch has a separate confirmation and is limited to the exact receipt-bound candidate executable SHA-256. Launch creates no acceptance/checkpoint authority. The launched candidate must independently pass Self-test and receive a separate explicit **Принять** confirmation for the fixed local Workbench Git checkpoint gate.

No update gate permits Git fetch/push or remote mutation, catalog mutation, Agent Execute/ActionPermit, Stable Core promotion, or arbitrary process paths supplied by JSON.
