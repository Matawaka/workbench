# Matawaka Workbench v0.51.11 — Owner→Lease Binding Transaction

## Goal

Make the provenance boundary between a COMMITTED MCP owner generation and exact read-lease binding crash-consistent and explicit.

`Prepared LeaseId != Lease Created != Owner Bound != Listener Ready`.

## Exact prepared LeaseId

v0.51.11 preallocates one safe exact `lease-*` identifier in `PREPARED_BINDING` before canonical lease state exists. The transaction therefore knows the exact recovery path even if the process dies immediately after canonical state materialization and before a later transaction update.

`PREPARED_BINDING` is name/evidence reservation only. It grants no read authority.

## Runtime order

`explicit start intent -> owner.lock -> reconcile prior binding -> v0.51.10 owner generation COMMITTED -> PREPARED_BINDING(exact LeaseId, canonical=false) -> exact v0.48-schema canonical creation under v0.51.5 index dirty + v0.51.6 fence -> LEASE_CREATED -> exact owner metadata LeaseId write -> OWNER_BOUND -> inherited clipboard/MCP listener flow`.

## Recovery

Before v0.51.10 may replace stale prior owner metadata, v0.51.11 reconciles the prior binding transaction under the already-held app owner lock.

- `PREPARED_BINDING` + exact state absent -> `ABANDONED_BEFORE_LEASE` (no canonical state observed at recovery; not a claim of historical nonexistence).
- `PREPARED_BINDING`/`LEASE_CREATED` + exact live canonical state but owner not bound -> `LIVE_ORPHAN_AFTER_LEASE_CREATE`; successor MCP owner generation is blocked, and the lease is **not** auto-revoked.
- the same exact lease after revoke/expiry/budget exhaustion -> terminal exact classification and successor startup may continue.
- exact prior owner SessionId + exact LeaseId metadata + exact canonical state -> `OWNER_BOUND_RECOVERED`.
- identity/schema/path mismatch -> fail closed before successor owner/lease/listener authority.

No historical lease enumeration is required because the exact LeaseId was prepared before canonical creation.

## Canonical creation corridor

The v0.51.11 prepared-ID creation service preserves:

- v0.48 state schema;
- v0.48 grant schema;
- v0.48 creation-receipt schema;
- bearer plaintext one-time return with SHA-only persistence;
- v0.51.5 active-index dirty-before-canonical-mutation protocol;
- v0.51.6 app-scoped cross-process index fence.

Canonical per-lease state remains the source of read authority. The prepared transaction is never authority.

## Authority boundary

The owner→lease transaction grants no lease/read/revoke/resume authority and discloses no bearer plaintext/hash or reusable endpoint path secret. `OWNER_BOUND` proves only an exact owner SessionId→LeaseId provenance relation; it does not prove listener readiness.

## Qualification

Windows hostile qualification covers normal PREPARED→LEASE_CREATED→OWNER_BOUND, crash before creation, crash immediately after exact canonical creation but before transaction update, live orphan fail-closed blocking without revoke, terminal exact recovery after revoke, OWNER_BOUND recovery, forged owner-session mismatch and same-app busy no-mutation. Public `main` remains deferred.
