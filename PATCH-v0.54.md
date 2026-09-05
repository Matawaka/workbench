# Workbench v0.54 — Bounded Runtime-Tree Materialization Lease

Accepted/public predecessor:

- `75f083f7f8383832ff85ecc61dd15ce47ba8d2c9`
- `workbench-v0.53.2-accepted`

v0.54 adds the missing generic authority boundary between the v0.52 exact artifact acquisition primitive and the v0.53 bounded runtime execution primitive:

`Verified ZIP Artifact != Materialized Runtime Tree != Execution Authority`.

The materialization request binds only Workbench-owned terminal v0.52 acquisition receipt evidence and exact selected `ArtifactId` values. Preview revalidates local archive bytes and derives a deterministic ZIP central-directory plan without extraction or filesystem mutation. After explicit confirmation, a one-shot materialization lease is consumed before the staging runtime root is created.

Unsafe ZIP paths, symlink/reparse entries, Windows canonicalization hazards, case-insensitive/cross-archive collisions and explicit file/expanded-byte ceiling excess fail closed. Exact planned files are written with create-new semantics into a unique sibling staging root, individually SHA-256 hashed, rebound into a deterministic tree digest, and promoted only after complete verification.

Success writes a v0.53-compatible `matawaka.runtime-tree-manifest/v0.53` in `MATERIALIZED_VERIFIED` state. That manifest is evidence input for the unchanged v0.53 execution preview; it does not grant process start, runtime readiness, benchmark, model request or game authority.

Explicit non-effects:
- no network access or artifact acquisition;
- no process start/stop;
- no shell/cmd/PowerShell/script/installer execution;
- no elevation;
- no PATH/registry/global environment mutation;
- no Git remote publication;
- no catalog mutation / Agent Execute / ActionPermit;
- no KONTUR-specific behavior or real LM3-A/LM1 authority.

Remote publication remains deferred until one tiny non-KONTUR real-host ZIP materialization smoke passes after local v0.54 acceptance.
