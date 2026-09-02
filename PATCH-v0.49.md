# Workbench v0.49 — Lease-Gated Read-Only MCP Adapter

## Predecessor

- commit: `2b0e3127e3c0284ff569720d85889cec3374f053`
- tag: `workbench-v0.48-accepted`

## Target

- semantic version: `0.49.0`
- accepted tag: `workbench-v0.49-accepted`

## Purpose

v0.47 proved a human-gated clipboard read relay. v0.48 proved short-lived, revocable read leases with exact app/role/path scopes, byte/call ceilings, expiry and hash-only bearer persistence. v0.49 adds a **local read-only MCP Streamable HTTP adapter** that can consume only an already-active v0.48 lease.

The four-button top-level Workbench surface remains unchanged. Adapter actions live under `Local apps`.

## Authority chain

```text
Human creates v0.48 read lease
-> exact LeaseId + bearer grant
-> human explicitly starts v0.49 adapter
-> loopback-only MCP request
-> fixed in-memory ApplicationId/LeaseId/bearer
-> v0.48 AuthorizeAndReadAsync
-> bounded local-app read
-> MCP response
```

The MCP caller never receives an argument that can select another application or lease. `ApplicationId`, `LeaseId` and bearer are fixed in the adapter session at startup and are omitted from the MCP tool schema.

```text
MCP Request != Filesystem Authority
MCP Adapter Authority <= Active Read Lease Authority
Adapter Startup != Lease Creation
Lease Bearer != MCP Tool Argument
```

## Official MCP SDK

The Workbench App pins the official C# MCP HTTP package:

`ModelContextProtocol.AspNetCore 2.2.0`

The adapter uses Streamable HTTP and exposes one content tool:

`read_local_app_chunk(role, relativePath, offset, maxBytes, expectedFileSha256?)`

Every invocation delegates to accepted `LocalAppReadLeaseV048Service.AuthorizeAndReadAsync`; the adapter does not implement a second filesystem authorization system.

## Listener boundary

After explicit adapter-start confirmation, Workbench creates:

- one Kestrel listener on IPv4 loopback `127.0.0.1` only;
- an OS-selected ephemeral port;
- a random 256-bit endpoint path token;
- one endpoint `/mcp/<random-token>`.

The start receipt persists only SHA-256 of the endpoint token. The plaintext endpoint is copied to the local clipboard so the operator can test it and, separately, configure an externally supported tunnel if desired.

```text
Loopback Listener != Public Listener
Local MCP Endpoint != Automatic Secure Tunnel
```

The adapter adds a host-header boundary permitting only `127.0.0.1` or `localhost` and rejects additional listener addresses during startup verification.

## Bearer handling

The v0.48 bearer is validated against hash-only lease state before startup. It is held only in the in-process adapter session and is not persisted in v0.49 start/stop receipts. Stop clears the Workbench-held bearer string reference; this is **not** a claim of guaranteed managed-memory zeroization.

## Stop and lease changes

`Stop read-only MCP adapter` stops the local listener and clears the in-memory session reference. Workbench window close performs best-effort listener shutdown.

The adapter does not create, renew or widen leases. If a lease expires, is revoked, becomes exhausted, goes out of scope or fails expected SHA validation, the underlying v0.48 gate refuses the MCP read.

```text
Lease Expiry/Revocation/Exhaustion => MCP Read Refuse
Expected Hash Mismatch => Refuse, Not Guess
Read Tool != Mutation/Execution/Write Authority
```

## Tunnel boundary

v0.49 deliberately performs **no Secure MCP Tunnel creation**, account login, connector installation or public endpoint publication. The generated loopback endpoint is only *tunnel-ready*. Whether the current ChatGPT account/product exposes a supported tunnel/connect flow is an independent activation-time observation.

```text
Local MCP Endpoint != Automatic Secure Tunnel
Secure Tunnel Availability != Account Entitlement
```

The v0.47 clipboard relay remains a conservative fallback.

## Publication boundary

Runtime lease state, bearer plaintext, endpoint token plaintext, private Apps/AppSources contents and MCP response data remain outside the Workbench Git source frontier and are not published by `Publish accepted`.
