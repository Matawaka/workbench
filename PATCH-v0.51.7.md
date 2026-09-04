# Matawaka Workbench v0.51.7 — Cross-Process MCP Session Ownership

## Scope

v0.51.7 closes the remaining process-local MCP runtime ownership gap after v0.51.6 cross-process active-index serialization.

For each `(WorkspaceRoot, ApplicationId)`, local read-only MCP runtime ownership is singular across simultaneously running Workbench processes.

## Runtime order

Auto-start:

`explicit human confirmation -> acquire app MCP ownership -> create exact indexed lease -> bind owner to LeaseId -> start inherited v0.49 loopback listener -> mark owner ready`

Normal closure:

`prove listener stopped -> release MCP ownership -> exact-revoke bound LeaseId through inherited verified-index lifecycle`

Same-app ownership contention fails with `MCP_SESSION_OWNED_BY_OTHER_PROCESS` before replacement lease creation.

## Recovery

Process termination releases the OS/file-handle runtime owner only. It does not revoke, renew or resume the canonical read lease. A surviving live lease remains subject to the existing expiry/orphan/exact-closure semantics.

Stale owner metadata is non-authoritative and cannot be reused as bearer, endpoint or resume authority.

`Owner record != lease authority != MCP resume authority`.

## Destructive-action exclusion

Exact orphan closure and revoke-all recovery first require a free app MCP ownership domain. A second Workbench process therefore cannot revoke authority underneath a live MCP listener owned by another process.

## Secret boundary

MCP owner metadata/receipts store neither:

- bearer plaintext;
- bearer hash;
- reusable endpoint path token.

Only non-secret runtime identity such as ApplicationId, exact LeaseId, process/session identity and loopback host/port may be recorded.

## Preserved boundaries

- canonical v0.48 read-lease state remains authority source of truth;
- v0.51.5 active index remains derived control state;
- v0.51.6 active-index cross-process fence is unchanged;
- v0.49 MCP listener/tool implementation is reused rather than reimplemented;
- historical lease evidence is not deleted or compacted;
- no automatic lease renewal/revocation after crash;
- no automatic Secure MCP Tunnel, network publication, catalog mutation, Agent Execute or ActionPermit authority;
- top-level four-button operator surface remains unchanged;
- `Publish accepted` remains locally deferred.

## Qualification

Windows two-process hostile qualification covers same-app contention, different-app independence, owner-process kill, surviving canonical lease authority, stale-metadata secret exclusion, fail-closed release without listener-stop proof, retained ownership after refused release and normal release ordering.
