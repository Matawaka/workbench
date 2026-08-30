# Matawaka Workbench v0.8

Windows-first, Codex-independent workbench for bounded Matawaka catalog analysis.

v0.8 preserves the accepted v0.7 execution boundary unchanged:

- verified fixed `SemanticHost.exe`;
- restricted primary token with maximum privileges disabled;
- Low integrity (`S-1-16-4096`);
- Windows Job Object containment;
- child runtime security attestation verified before semantic input;
- bounded stdin/stdout IPC;
- no network isolation or OS-sandbox claim;
- Execute denied; mutation budget remains zero.

## New in v0.8

The **Self-test** button runs an automated acceptance matrix:

1. read-only propose with `local-contract-synthesis-v0.3`;
2. the same bounded input with `deterministic-evidence-semantic-v0.2`;
3. denied Execute with a check that evidence/semantic processing never opens.

A typed receipt is saved under `Workbench/artifacts/acceptance` and shown in the
**Acceptance** tab.

`Automated Acceptance != Canonical Conformance`.
`Passing Self-Test != New Authority`.
