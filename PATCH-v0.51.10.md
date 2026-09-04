# Matawaka Workbench v0.51.10 — MCP Owner Generation Transaction Closure

## Goal

Make the provenance boundary between prior-owner evidence preservation and successor owner-metadata materialization crash-consistent and explicit.

`PREPARED != COMMITTED`.

v0.51.9 already guarantees prior owner metadata is preserved before overwrite. v0.51.10 additionally records whether a successor owner generation was only prepared, actually materialized, recovered after a crash, abandoned before successor write, or closed with active metadata absent.

## Runtime order

`explicit start intent -> acquire owner.lock -> reconcile prior generation transaction -> preserve/reuse exact prior evidence -> PREPARED -> successor owner metadata write -> COMMITTED -> inherited lease creation/bind/listener flow`.

The owner is not returned to the existing read-session UI until COMMITTED has been recorded from exact successor owner metadata observation.

## Recovery states

- `PREPARED`: prior evidence is bound to an exact successor SessionId, but successor metadata is not yet proven.
- `ABANDONED_BEFORE_SUCCESSOR`: exact prior metadata SHA is still active; the prior archive is verified and reusable.
- `COMMITTED`: exact successor owner metadata contract/session was observed after PREPARED.
- `COMMITTED_RECOVERED`: a later acquisition observed exact successor metadata for a surviving PREPARED transaction and closed the crash gap without inferring lease/listener activity.
- `CLOSED_METADATA_ABSENT`: active metadata is absent; the prior PREPARED attempt is closed as evidence-only without guessing whether a successor committed.

Unexpected PREPARED/archive/metadata combinations fail closed with `MCP_OWNER_GENERATION_TRANSACTION_INCONSISTENT` before new lease/listener authority.

## Evidence deduplication

New v0.51.9 prior-owner archives are content-addressed by exact SHA-256 under `generation-evidence-v0519/by-sha/`. A retry before PREPARED therefore reuses the same exact prior bytes instead of producing duplicate archive payloads. v0.51.10 may also reuse an exact verified legacy v0.51.9 archive path from an abandoned PREPARED transaction.

## Authority boundary

Generation transaction state is provenance/control evidence only. It grants no:

- lease authority;
- read authority;
- revoke authority;
- MCP resume authority;
- bearer/hash/endpoint-secret disclosure;
- network/tunnel/publication/catalog/Agent Execute/ActionPermit authority.

Canonical v0.48 lease state remains authority source of truth. v0.51.5 active index remains derived. v0.51.6 index fence, v0.51.7 singular MCP ownership, v0.51.8 status/recovery, and v0.51.9 owner-generation evidence semantics remain preserved.

## Qualification

Windows hostile qualification covers normal COMMITTED flow, abandoned PREPARED recovery with archive reuse, pre-PREPARED content-addressed deduplication, COMMITTED_RECOVERED, metadata-absent closure, inconsistent metadata refusal, archive hash mismatch refusal, same-app busy no-mutation, authority/secret non-effects, and deferred public main.
