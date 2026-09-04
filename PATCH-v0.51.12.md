# Matawaka Workbench v0.51.12 — Listener Readiness Transaction

## Frontier

v0.51.11.1 closes exclusive Local Apps routing and proves the intended v0.51.11 owner→lease binding path on the real host. The remaining startup provenance gap is between `OWNER_BOUND` and a materially ready local MCP listener.

## Core distinction

`OWNER_BOUND != LISTENER_START_ATTEMPTED != LISTENER_READY`.

A listener-readiness transaction is evidence/control state only. It grants no lease, read, revoke, resume, tunnel, publication, catalog, Agent Execute or ActionPermit authority.

## Intended order

`OWNER_BOUND -> PREPARED_LISTENER_START -> adapter StartAsync -> exact loopback listener observation -> LISTENER_READY -> inherited MarkListenerReadyAsync`.

`PREPARED_LISTENER_START` is not proof that a listener exists. `LISTENER_READY` is not proof of public/external reachability; the listener remains IPv4 loopback-only unless a separate explicit tunnel action is authorized.

## Recovery

Recovery is exact-bound to ApplicationId + owner SessionId + LeaseId + owner→lease binding transaction. It does not enumerate historical canonical lease state and does not auto-start, auto-revoke, auto-renew or auto-resume.

Target classifications include `ABANDONED_BEFORE_LISTENER`, `LIVE_BOUND_NO_LISTENER`, `LEASE_TERMINAL_BEFORE_LISTENER`, `LISTENER_READY_RECOVERED` and fail-closed inconsistent evidence.

## Preserved boundaries

- canonical v0.48 lease state remains authority source of truth;
- v0.51.5 active index remains derived control state;
- v0.51.6 index fence unchanged;
- v0.51.7 cross-process MCP ownership unchanged;
- v0.51.9/v0.51.10 owner-generation evidence unchanged;
- v0.51.11 owner→lease binding unchanged;
- v0.51.11.1 exclusive Local Apps routing preserved;
- no bearer plaintext/hash or reusable endpoint path token in readiness evidence;
- public `main` remains deferred.
