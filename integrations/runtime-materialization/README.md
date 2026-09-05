# Workbench v0.54 — bounded runtime-tree materialization

v0.54 fills the provider-neutral boundary between the already-admitted v0.52 artifact acquisition primitive and the already-admitted v0.53 runtime execution primitive.

```text
Verified ZIP Artifact
        !=
Materialized Runtime Tree
        !=
Execution Authority
```

## Input authority

A materialization request does not accept an arbitrary archive path. It binds an exact Workbench-owned v0.52 `ArtifactAcquisitionExecutionReceiptV052` by path and SHA-256 and selects exact `ArtifactId` values from that receipt. The selected local ZIP bytes are re-sized and re-hashed before planning and again before extraction.

Preview is read-only. It derives a deterministic plan from ZIP central-directory metadata and refuses unsafe Windows paths, traversal/rooted/ADS paths, reserved device names, trailing-dot/space canonicalization hazards, symlink/reparse entries, case-insensitive collisions, file/directory prefix collisions and explicit file/expanded-byte ceiling excess.

## One-shot materialization

After explicit operator confirmation, one one-shot materialization lease is created. Its call budget is durably consumed before the staging runtime root is created.

Exact planned files are extracted with create-new semantics into a unique sibling staging root. Every output file is SHA-256 hashed and must reach the ZIP-declared exact length. The complete staging tree is deterministically hashed before Workbench writes a v0.53-compatible runtime-tree manifest:

```text
Schema = matawaka.runtime-tree-manifest/v0.53
Version = 0.53
State = MATERIALIZED_VERIFIED
```

The staging directory is atomically renamed to the new final runtime root only after complete verification, then the promoted tree and manifest are reverified.

## Non-effects

v0.54 does not perform network access, artifact acquisition, process start/stop, shell/script/installer execution, elevation, PATH/registry/global-environment mutation, benchmark, model request, game access, Git publication, catalog mutation, Agent Execute or ActionPermit creation.

`MATERIALIZED_VERIFIED` is evidence that exact verified archive bytes were expanded into an exact verified local tree. It is not runtime readiness or execution/model authority.

## KONTUR

KONTUR may later supply an exact caller/handoff for already-acquired LM3-A llama.cpp/CUDA ZIPs. v0.54 itself is not KONTUR-specific and grants no authority to download or materialize the real LM3-A archives. LM1 GGUF weights are not ZIP runtime-tree materialization inputs.
