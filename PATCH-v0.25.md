# Workbench v0.25 — Recovery Evidence Export / Import Capsule Boundary

v0.25 adds two post-acceptance evidence-transport gates over the already proven v0.24 relocated replay capsule.

`Recovery export` verifies a passing retained v0.24 relocation drill, re-binds the exact five replay-capsule JSON files and their SHA-256 values, then writes one self-contained local transport ZIP plus an export receipt under `artifacts/recovery-transports`.

`Recovery import` first inspects a user-selected transport ZIP without mutation, requires the exact transport manifest and five capsule files, re-verifies all bytes and recovery evidence semantics, and only after explicit UI confirmation copies those evidence bytes into a disjoint `.workbench/recovery-capsule-imports` root and writes an import receipt.

The transport boundary proves a local ZIP serialization/deserialization and import-verification property. It does **not** authenticate the original evidence producer, does not prove cross-machine or cross-OS portability, does not execute recovery, and does not grant rollback, deletion, source mutation, build, checkpoint, network, catalog, Agent Execute, general recovery-claim, automatic recovery, or Stable Core authority.
