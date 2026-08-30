# Matawaka Workbench v0.26 — local transport-capsule independence drill

Status: additive Workbench maintenance/evidence layer. No UU-AAP Stable Core promotion.

## Purpose

v0.25 established a self-contained local recovery-evidence transport ZIP and a verify-only import boundary. v0.26 adds one post-acceptance independence drill to test a narrower property:

> after a passing v0.25 transport ZIP is byte-bound and copied into a disjoint local Workbench root, recovery-evidence inspection, semantic replay, and exact evidence materialization can proceed from the copied transport bytes only, without dereferencing the original replay/relocation/evidence roots.

This is a same-machine application-level path-independence check. It is **not** OS filesystem isolation, producer authentication, cross-machine portability, cross-OS portability, production-main recovery proof, or live recovery authority.

## New surface

After `workbench-v0.26-accepted`, the GUI exposes **Transport independence**.

The drill:

1. requires a clean accepted v0.26 main Workbench repository;
2. locates one retained passing v0.25 import receipt;
3. re-binds the exact source transport ZIP SHA-256 and manifest digest;
4. copies the exact ZIP bytes into `.workbench/recovery-transport-independence/...`;
5. starts a transport-only replay phase in which the verifier is given only the copied ZIP path;
6. replays the transport semantic bindings and reproduces transport/capsule/evidence digests;
7. materializes the six exact transport entries under the drill root and re-verifies SHA-256/length;
8. proves the main Workbench Git HEAD/tag/dirty set is unchanged;
9. writes one retained drill receipt under `artifacts/recovery-transport-independence`.

## Authority boundary

The drill does **not** authorize:

- Workbench source mutation;
- source restore or rollback;
- deletion or modification of retained source evidence/transport;
- `dotnet` restore/build/test/publish;
- Git add/commit/tag outside the ordinary later checkpoint gate;
- Git fetch/push or remote mutation;
- network access;
- Matawaka catalog mutation;
- Agent Execute or ActionPermit creation;
- automatic recovery;
- producer-authentication claims;
- cross-machine/cross-OS portability claims;
- Stable Core/interface-registry promotion.

The path guard is application-level and must not be described as an OS sandbox.

## Expected post-acceptance receipt

A passing drill uses:

- schema `matawaka.workbench-recovery-evidence-transport-independence-drill/v0.26`;
- status `INDEPENDENT_LOCAL_TRANSPORT_CAPSULE_VERIFIED`;
- `CopiedTransportByteIdentical=true`;
- `CopiedTransportSeparatedFromSourceTransportRoot=true`;
- `CopiedTransportInspectionVerified=true`;
- `TransportManifestDigestReproduced=true`;
- `CapsuleManifestDigestReproduced=true`;
- `EvidenceEnvelopeDigestReproduced=true`;
- `IndependentMaterializedCopiesVerified=true`;
- `ReplayUsedOnlyCopiedTransportBytes=true`;
- `OriginalTransportZipRequiredAfterCopy=false`;
- `OriginalRelocationRootRequiredForDrill=false`;
- `OriginalReplayRootRequiredForDrill=false`;
- `OriginalEvidenceArtifactsRequiredForDrill=false`;
- `HistoricalAbsolutePathsDereferencedDuringTransportReplay=false`;
- `MainRepositoryUnchanged=true`;
- all live recovery/build/network/catalog/Agent Execute/Stable Core authority flags remain false.

## Acceptance sequencing

The v0.26 Self-test does not run the independence drill. First accept the v0.26 candidate through the existing explicit GUI checkpoint gate. Only then run **Transport independence** with a separate explicit confirmation.
