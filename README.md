# Matawaka Workbench v0.13

Windows/.NET 10 Workbench for bounded local Matawaka analysis and explicitly separated maintenance gates.

Current path:

- persistent `K:\Matawaka` workspace and local catalog observation;
- typed read-only agent authority and balanced evidence frontier;
- interchangeable offline semantic providers;
- fixed verified SemanticHost with restricted Low-integrity token, Job Object and runtime attestation;
- PCL-compatible visible liveness;
- automated two-provider + denied-Execute Self-test;
- explicit GUI-local accepted checkpoint;
- relevant UU-AAP source-set binding independent of unrelated repository HEAD drift;
- bounded manifest update intake;
- explicit staging-only materialization;
- read-only staged `Add/Replace/NoOp` plan;
- explicit exact source apply + fixed local `dotnet --no-restore` build/publish;
- separate receipt-bound candidate launch.

## Update authority chain

A valid update never acquires downstream authority automatically:

`Valid Package != Materialization Authority != Source Apply/Build Authority != Launch Authority != Checkpoint Authority`

### Plan

**Пакет обновления** validates a bounded local ZIP, predecessor tag/commit, exact file set and SHA-256s. It creates no materialization/build/checkpoint authority.

### Materialize

**Материализовать** requires a READY plan plus explicit human confirmation. Exact payload bytes are copied only under ignored `.workbench/update-materializations` and reverified.

### Plan source delta

**План применения** re-verifies staging and reports exact `Add`, `Replace`, `NoOp` effects without changing tracked source.

### Apply + build

**Применить + собрать** requires a fresh READY staged plan and another explicit confirmation. Only exact planned source paths may change. Replacements are backed up. The only build executable is `<workspace>/.dotnet-sdk/dotnet.exe`, with fixed `build/publish --no-restore` operations. Any failed transaction restores accepted predecessor source.

This gate requests no network operation, but does not claim OS network isolation.

### Launch + accept

**Запустить candidate** is a separate explicit action limited to the exact built executable digest. Launch is not acceptance. The candidate must separately pass Self-test and **Принять** before the Workbench repository receives a local accepted commit/tag.

No maintenance gate creates authority over Matawaka catalog repositories or Agent Execute. Workbench receipts are local evidence, not canonical UU-AAP conformance.
