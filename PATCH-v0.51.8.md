# Matawaka Workbench v0.51.8 — MCP Ownership Status + Stale Owner Recovery Surface

## Goal

Make cross-process local MCP runtime ownership explicitly observable without turning owner metadata, a free owner domain, or archived evidence into read/lease/revoke/resume authority.

## Ownership status

For a selected registered application, `MCP Ownership Status` distinguishes:

- `OWNED` — the app-scoped v0.51.7 owner handle is currently held by some process;
- `FREE_NO_METADATA` — no live owner handle and no active owner metadata slot;
- `FREE_STALE_METADATA` — the owner handle is free while non-authoritative v0.51.7 metadata remains.

The status probe opens an existing `owner.lock` only with `FileMode.Open + FileAccess.Read + FileShare.None`. It never creates the lock file and performs no owner/lease/index mutation.

If valid stale metadata contains an exact `LeaseId`, status reads only that canonical state path and classifies it as `LIVE_ORPHAN`, `LIVE_OWNER_DOMAIN_BUSY`, `REVOKED`, `EXPIRED`, `BUDGET_EXHAUSTED` or `ABSENT`. No historical lease enumeration is performed.

## Explicit stale metadata acknowledgement

`Acknowledge stale MCP owner metadata` is a separate confirmed action. It:

1. requires a fresh `FREE_STALE_METADATA` observation;
2. acquires an exclusive guard on the existing app `owner.lock` so another process cannot claim MCP ownership during rotation;
3. re-verifies the exact metadata bytes did not change;
4. moves those exact bytes into `stale-evidence-v0518` and verifies SHA-256;
5. clears only the active `owner-v0.51.7.json` slot;
6. writes a receipt that grants no resume/read/lease/revoke authority.

If the referenced canonical lease is still live, it remains live/orphan after acknowledgement. Existing exact orphan closure remains a separate explicit action.

## Invariants

- `Owner metadata != live owner handle`.
- `Stale owner metadata != MCP resume authority`.
- `Free MCP domain != permission to create/revoke/resume a lease`.
- `MCP ownership status != canonical lease authority status`.
- `Acknowledgement != lease closure`.
- `Evidence rotation != authority mutation`.

## Preserved boundaries

- canonical v0.48 lease state remains authority source of truth;
- v0.51.5 active index remains derived control state;
- v0.51.6 active-index cross-process fence remains unchanged;
- v0.51.7 singular MCP ownership and stop -> owner release -> exact revoke ordering remain unchanged;
- no bearer plaintext/hash or reusable endpoint path token is disclosed by status/receipt;
- no automatic MCP resume or lease renewal/revocation after crash;
- no historical evidence deletion/compaction;
- no automatic Secure MCP Tunnel, network publication, catalog mutation, Agent Execute or ActionPermit authority;
- top-level four-button operator surface remains unchanged;
- `Publish accepted` remains deferred.
