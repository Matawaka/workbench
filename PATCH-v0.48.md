# Workbench v0.48 — Bounded Read Session Leases

## Predecessor

- commit: `88505162a2ff1f9aeb5ca45bff876d9f0679b073`
- tag: `workbench-v0.47-accepted`

## Target

- semantic version: `0.48.0`
- accepted tag: `workbench-v0.48-accepted`

## Purpose

v0.47 proved a real human-gated chat read round-trip. v0.48 adds a transport-neutral short-lived read authority so a later adapter can perform several bounded reads without turning one approval into general filesystem access.

The four-button top-level Workbench surface remains unchanged. Registered-app actions remain under `Local apps`.

## Read session lease

A lease is created only from an explicitly selected registered application and a separately confirmed request:

- schema `matawaka.local-app-read-lease-request/v0.48`;
- exact selected `ApplicationId`;
- one or more explicit scopes using role `installed|source` plus exact file or existing directory-prefix path;
- maximum 1 MiB per read;
- maximum 8 MiB total;
- maximum 32 calls;
- maximum 900 seconds / 15 minutes.

The preview is content-free. It validates scopes and ceilings but creates no bearer, lease or read authority.

After explicit confirmation Workbench creates a random lease id and a random 256-bit bearer. The bearer is returned in the local grant for the operator; only its SHA-256 is persisted in ignored local lease state.

```text
Lease Request != Lease Authority
Bearer Possession != Authority Beyond Lease Bounds
Lease Authority != General Filesystem Authority
```

## Lease consumption

`LocalAppReadLeaseV048Service.AuthorizeAndReadAsync` is the reusable future-adapter entry point. A call must pass:

- exact lease id + bearer;
- exact application id;
- `installed|source` role;
- a relative path admitted by a lease scope;
- offset and bounded max bytes;
- optional expected whole-file SHA-256.

The call delegates the actual content read to the already accepted bounded local read/relay primitives. It refuses expired, revoked, exhausted, wrong-bearer, out-of-scope, traversal/reparse and stale-hash requests. Successful calls atomically consume one call and the actual number of returned bytes from persisted local budgets.

## Revocation

`Revoke active read leases` explicitly revokes all currently active leases for the selected app. Revocation changes only local ignored lease state.

## Transport boundary

v0.48 intentionally implements **no** HTTP listener, direct network listener, tunnel or MCP server. The existing v0.47 `Chat read relay` remains available as a conservative manual fallback.

A later adapter may expose lease-gated reads through a read-only MCP/Secure MCP Tunnel path, but such a transport is a separate authority and must not bypass v0.48 lease checks.

```text
Lease != Network Listener/Tunnel/MCP Authority
Read Lease != Mutation/Execution/Write Authority
Lease Expiry/Revocation/Exhaustion => Refuse
Expected Hash Mismatch => Refuse, Not Guess
```

Mutable lease state and bearer material are outside the Workbench Git source frontier and are not published by `Publish accepted`.
