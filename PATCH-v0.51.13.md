# Workbench v0.51.13 — Shutdown Transaction

## Semantic delta

v0.51.12 proved the forward runtime chain:

`OWNER_BOUND -> PREPARED_LISTENER_START -> LISTENER_STARTED -> LISTENER_READY`.

v0.51.13 closes the reverse crash/provenance gap by separating:

`LISTENER_READY -> SHUTDOWN_PREPARED -> LISTENER_STOPPED -> OWNER_RELEASED -> LEASE_REVOKED/LEASE_ALREADY_TERMINAL -> SHUTDOWN_COMPLETED`.

## Invariants

- `SHUTDOWN_PREPARED != LISTENER_STOPPED`.
- `LISTENER_STOPPED != OWNER_RELEASED`.
- `OWNER_RELEASED != LEASE_REVOKED`.
- Shutdown transaction evidence grants no read/revoke/resume authority.
- Owner release requires materially observed listener inactivity.
- Exact lease revoke remains a separate inherited authority-bearing operation after owner release.
- Sibling leases are outside this shutdown corridor.
- Recovery never auto-starts/resumes a listener and never auto-revokes/renews a live lease.
- Recovery uses one exact LeaseId path and performs no historical canonical enumeration.
- Bearer plaintext/hash and reusable endpoint path token remain omitted.

## Recovery

When a new process reacquires `owner.lock`, v0.51.13 shutdown recovery runs before v0.51.12 listener-readiness, v0.51.11 binding and v0.51.10 generation reconciliation.

A still-live exact lease becomes `OWNER_RELEASED_LEASE_LIVE` and blocks silent successor startup until explicit closure or expiry. A terminal exact lease becomes `LEASE_ALREADY_TERMINAL` without rewriting canonical state.

## KONTUR relevance

This layer remains generic Workbench infrastructure. It is the reverse lifecycle primitive needed before future local-runtime reuse for KONTUR/`llama.cpp`:

`Runtime Ready != Stop Requested != Runtime Stopped != Authority Closed`.

KONTUR integration anchors remain planning/contracts only and create no download/model/runtime/game authority.

## Delivery boundary

Exact local predecessor for eventual update package:

- tag: `workbench-v0.51.12-accepted`
- commit: `9eb536894dbe16d247e995876facd669121ed8f2`

Remote qualified source predecessor:

- `4eb247276908c675cec86aa4428c94d148dbb3bc`

Public `main` remains deferred.
