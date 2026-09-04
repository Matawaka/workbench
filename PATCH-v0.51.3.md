# Workbench v0.51.3 — Read Session Status + Exact Orphan Closure

v0.51.3 extends the local lease-gated read lifecycle with restart-safe observability and exact orphan cleanup without persisting bearer plaintext.

## Operator flow

Normal session remains:

`Read session lease -> local MCP ready -> End Read Session -> exact bound LeaseId revoked`.

Restart/orphan path becomes:

`Read Session Status -> inspect lease metadata only -> choose exact orphan LeaseId -> explicit exact revoke`.

## Status boundary

Status may show only local control metadata already present in lease state: ApplicationId, LeaseId, scope, issue/expiry times, remaining call/byte budget, revoked/expired state, active-MCP binding, and orphan-closure eligibility.

Status must not disclose bearer plaintext, bearer hash, application file contents, hashes, timestamps or ACLs, and it creates no new lease/session authority.

## Orphan closure boundary

A live lease is exact-orphan-closure eligible only when it is not revoked/expired, retains budget, and is not bound to the active local MCP adapter. The operator selects and explicitly confirms the exact LeaseId.

Closure uses the existing v0.51.2 exact-revoke primitive; it does not call revoke-all, stop/change an unrelated active MCP adapter, start/change Secure MCP Tunnel, or touch sibling leases.

## Failure / authority semantics

- no automatic startup revocation;
- no automatic retry;
- no bearer persistence or recovery;
- no sibling lease enumeration for mutation;
- no application/source/catalog mutation;
- no network/publication/Agent Execute/ActionPermit authority;
- existing revoke-all remains explicit recovery only.

## Publication boundary

Local checkpoint target is `workbench-v0.51.3-accepted`, dual-bound to the exact update/build source manifest predecessor plus `workbench-v0.51.2-accepted` at current HEAD. Public remote publication remains deferred while the external bridge path is paused.
