# Workbench v0.51.1 — Read Lease → Local MCP Auto-Start

Local predecessor:
- `a8a93143e942a02913475013e355d61b2fa6bee8`
- `workbench-v0.51-accepted`

Public remote frontier remains intentionally unchanged at v0.50.2 while the external ChatGPT bridge admission is unresolved.

## Goal

Reduce the repeated operator sequence:

```text
Read session lease
-> copy grant
-> Local apps
-> Start local MCP
-> paste grant
-> confirm
```

to one explicit local action:

```text
Read session lease
-> exact grant JSON copied to clipboard
-> exact clipboard round-trip verified
-> local MCP adapter starts automatically
```

## Authority boundary

The existing human confirmation for `Read session lease` is the authority source for this combined local workflow.

The automatic continuation:
- does not create a second lease;
- does not renew or widen the lease;
- cannot change ApplicationId, scopes, TTL, max calls, max bytes/read or total byte budget;
- accepts only the exact just-created grant;
- binds the local MCP runtime to the exact same ApplicationId, LeaseId and bearer;
- does not start Secure MCP Tunnel or outbound HTTPS.

The underlying v0.48 lease gate and v0.51 read/browse tool semantics are unchanged.

## Clipboard handoff

After lease creation Workbench serializes the exact grant JSON and writes it to the Windows clipboard.

Before MCP startup it immediately reads clipboard text back and requires ordinal exact string equality with the serialized grant. A mismatch refuses automatic MCP startup.

The clipboard remains useful to the operator as a recovery handoff. Workbench still persists only the bearer hash in lease state/receipts.

## Local MCP startup

After exact clipboard verification Workbench re-runs the existing v0.49 adapter preview against the active lease and requires:
- exact ApplicationId;
- exact LeaseId;
- bearer SHA-256 matching the just-created lease receipt.

Only then is the existing local MCP adapter started.

The adapter remains:
- IPv4 loopback only;
- random secret endpoint path;
- no public listener;
- no outbound network;
- exactly the v0.51 read-only tool surface:
  - `read_local_app_chunk`
  - `list_local_app_entries`.

The secret local MCP endpoint is held only in Workbench memory and is not copied over the lease grant in the clipboard.

## Existing-active adapter rule

The combined action refuses **before creating a new lease** when another local MCP adapter or Secure MCP Tunnel is still active in the current Workbench process. This avoids minting an unnecessary lease that cannot be consumed by the process-global adapter.

## Partial failure

If the lease is created successfully but clipboard verification or MCP startup then fails:

```text
LEASE_CREATED_MCP_START_FAILED
```

- the lease is preserved;
- no automatic retry occurs;
- no automatic revoke occurs;
- no tunnel starts.

The operator may either:
1. manually select `Start local MCP` and use the exact grant JSON still in clipboard while the lease remains fresh; or
2. explicitly `Revoke active read leases`.

## Preserved manual controls

The existing controls remain available:
- manual `Start local MCP`;
- `Stop local MCP`;
- `Revoke active read leases`;
- Secure MCP Tunnel actions (not used automatically by v0.51.1).

## Publication boundary

v0.51.1 may be locally accepted for development and local MCP use. `Publish accepted` is deliberately deferred while public v0.51 admission remains blocked by the external ChatGPT custom-app/tool-snapshot issue. Clicking Publish performs no GitHub mutation.

## Invariants

- `Lease Confirmation -> Local MCP Startup` does not mean `Lease Confirmation -> Tunnel Authority`.
- `Clipboard Grant == Exact Created Grant` is required before auto-start.
- `Auto-Start Authority <= Created Lease Authority`.
- `Local MCP Reachability != Filesystem Authority`.
- `MCP Startup Failure != Automatic Lease Revoke`.
- `MCP Startup Failure != Automatic Retry`.
