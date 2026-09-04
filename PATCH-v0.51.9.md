# Matawaka Workbench v0.51.9 — MCP Owner Generation Continuity

## Goal

Prevent a newly acquired local MCP owner generation from silently overwriting stale active owner metadata from the prior generation.

## Ordering

`explicit MCP/read-session start intent -> acquire app-scoped owner.lock -> preserve/hash-verify prior active owner metadata if present -> write successor owner generation -> create/bind exact lease -> start inherited loopback MCP listener`.

Evidence preservation occurs under the already-held exclusive owner handle and before the successor active metadata slot is written.

## Prior metadata outcomes

- `NO_PRIOR_OWNER_METADATA` — no prior active metadata existed; successor generation continues normally.
- `PRIOR_OWNER_METADATA_PRESERVED_VALID` — exact v0.51.7 metadata bytes archived and SHA-256 verified; safe prior SessionId/LeaseId/state may be referenced in the transition receipt.
- `PRIOR_OWNER_METADATA_PRESERVED_OPAQUE_UNTRUSTED` — prior bytes did not satisfy the exact v0.51.7 non-authoritative contract; exact bounded bytes are still archived, but no prior fields become trusted authority.

Prior active metadata is bounded to 64 KiB. Reparse/oversize/archive/hash failures fail closed before successor metadata and before lease creation.

## Authority boundary

Generation evidence is provenance only. It grants no:

- lease creation/revocation/renewal authority;
- read/list authority;
- MCP resume/start authority beyond the already-explicit current start corridor;
- Secure MCP Tunnel/network/publication/catalog/Agent Execute/ActionPermit authority.

Canonical v0.48 lease state remains authority source of truth. v0.51.5 active index remains derived control state. v0.51.6 index fence, v0.51.7 singular MCP ownership, and v0.51.8 ownership status/stale acknowledgement remain preserved.

## Compatibility with v0.51.8 acknowledgement

If the operator explicitly acknowledges stale owner metadata before starting the next session, v0.51.9 sees no active prior metadata and records `NO_PRIOR_OWNER_METADATA`; it does not duplicate the v0.51.8 archive.

If the operator does not acknowledge it first, v0.51.9 automatically preserves the prior active metadata because a new generation must not destroy provenance merely by starting.

## Failure semantics

If prior-evidence preservation fails after owner.lock is acquired, the owner handle is released and the acquisition throws. Since v0.51.7/v0.51.8 UI acquires ownership before indexed lease creation, no successor lease or listener is created.

## Non-effects

- no historical canonical lease scan;
- no canonical lease or active-index mutation by generation preservation;
- no prior raw bytes copied into transition receipt/log/UI;
- no bearer plaintext/hash or endpoint path token disclosure;
- no additional top-level Workbench control;
- no second human confirmation solely for evidence preservation;
- public `main` remains deferred at v0.50.2 while external bridge admission remains paused.
