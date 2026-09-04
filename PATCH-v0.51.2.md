# Workbench v0.51.2 — End Read Session

v0.51.2 makes the local lease-gated read lifecycle symmetric without adding any OpenAI bridge dependency.

## Operator flow

Start remains v0.51.1:

`Read session lease -> exact grant clipboard round-trip -> local MCP auto-start`.

Closure becomes one explicit action:

`End Read Session -> local MCP stop -> exact bound LeaseId revoke -> runtime view clear`.

## Exact closure boundary

- End Read Session is enabled only for the application that owns the active local MCP adapter.
- Secure MCP Tunnel must already be stopped; outbound transport remains a separate authority.
- The exact `LeaseId` comes only from the active Workbench MCP runtime binding; the operator does not type/select another lease.
- Closure stops MCP before state revocation so no active MCP read/list state writer remains.
- `LocalAppReadLeaseExactRevokeV0512Service` addresses only the exact state file for `(ApplicationId, LeaseId)` and performs no sibling lease enumeration.
- Existing `Revoke ALL active read leases` remains an explicit recovery action and is not called by End Read Session.
- Expired exact leases may still be marked revoked to leave durable closure evidence.

## Failure semantics

- no automatic retry;
- if MCP stop/receipt has a problem, bounded best-effort stop is attempted;
- exact revoke is attempted only after the adapter is observed inactive;
- if exact revoke fails, the UI reports `END_READ_SESSION_PARTIAL` and points to the explicit revoke-all recovery path;
- if exact revoke succeeds, sibling lease revoke count is fixed at zero.

## Non-effects

No automatic Secure MCP Tunnel, OpenAI/ChatGPT/plugin action, network access, publication, catalog mutation, application/source mutation, arbitrary process launch, Agent Execute, ActionPermit, recursive browse, or new read authority.

## Publication/version boundary

This source branch is implementation/qualification only. Final v0.51.2 update packaging must be bound to the user's exact local `workbench-v0.51.1-accepted` commit. Public Workbench publication remains deferred while the external ChatGPT bridge path is paused.
