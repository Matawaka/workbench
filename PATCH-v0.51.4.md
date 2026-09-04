# Workbench v0.51.4 — Bounded Read Session Status View

v0.51.4 bounds the operator-facing lease-status representation while preserving every persisted lease-state evidence file.

## Representation rule

`Live authority != historical evidence`.

- all live leases are returned in full while their count is within the fixed hard ceiling (32);
- 33+ simultaneous live leases produce explicit fail-closed `LIVE_AUTHORITY_OVERFLOW` instead of a partial live-authority list;
- historical revoked/expired/exhausted leases are presented newest-first in pages;
- default history page = 16 records; hard maximum page = 64 records;
- pagination changes representation only and never changes lease state.

## Exact orphan closure

v0.51.3 orphan recovery is preserved. Exact closure now uses a pagination-independent exact LeaseId lookup before and after mutation, so a historical page cannot create or remove closure authority.

## Evidence preservation

No lease-state file is deleted, compacted, archived, rewritten or reclassified merely because a page is not currently displayed. Bearer plaintext/hash remain omitted.

## Explicit scope boundary

v0.51.4 still performs the v0.51.3 full lease-state classification pass before producing the bounded view. A future active-lease index may bound filesystem scan cost itself; that is deliberately not mixed into this layer because it would change writer/index semantics.

## Non-effects

No lease creation/renewal/scope widening, no automatic revoke, no read/list budget consumption, no application content read, no MCP/tunnel/network start, no remote publication, no catalog/source/app mutation, no Agent Execute or ActionPermit authority.

Public Workbench publication remains deferred.
