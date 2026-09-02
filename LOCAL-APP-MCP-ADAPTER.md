# Local App Lease-Gated Read-Only MCP Adapter

Workbench v0.49 introduced the local MCP transport concept above the accepted v0.48 read-lease authority. Real-host admission exposed one dependency-closure defect: the initial v0.49 candidate used `Microsoft.AspNetCore.App`, which was present in CI but not guaranteed on the accepted Windows host. v0.49.1 stabilizes the transport without widening update/network authority.

## Operator flow

1. `Local apps` -> select the registered app.
2. Create a narrow `Read session lease` if no suitable active v0.48 lease exists.
3. Keep the exact one-time v0.48 grant JSON containing `LeaseId` + bearer.
4. Choose `Start read-only MCP adapter`.
5. Paste that exact grant.
6. Review the content-free adapter preview: selected app, exact lease id, expiry, remaining budgets and scopes.
7. Explicitly confirm the loopback listener.
8. Workbench starts one MCP Streamable HTTP endpoint on `127.0.0.1:<ephemeral-port>/mcp/<random-256-bit-token>` and copies the exact local URL to the Windows clipboard.
9. Verify the local endpoint first.
10. Only if a supported ChatGPT/OpenAI tunnel/connect path is available and disclosure is intended, configure that separately.
11. Use `Stop read-only MCP adapter` and/or `Revoke active read leases` when the session should end.

## MCP tool

The content surface intentionally contains one tool: `read_local_app_chunk`.

Arguments exposed to the MCP caller are only `role`, `relativePath`, `offset`, `maxBytes`, and optional `expectedFileSha256`. The caller does **not** supply `ApplicationId`, `LeaseId`, lease bearer or filesystem root; those remain fixed in the human-approved v0.48 adapter session.

Every tool invocation delegates to `LocalAppReadLeaseV048Service.AuthorizeAndReadAsync`, so MCP cannot bypass selected app, role/path scopes, reparse/path boundaries, TTL, call/byte ceilings, revocation or expected SHA.

```text
MCP Request != Filesystem Authority
MCP Adapter Authority <= Active Read Lease Authority
Bearer Possession != Authority Beyond Lease Bounds
```

## Runtime protocol and dependency closure

The accepted Workbench updater builds the successor with `--no-restore` and creates no package-download or shared-framework-install authority. CI availability of an assembly is not evidence that the accepted user host has that runtime.

The initial v0.49 Kestrel implementation therefore failed real-host admission with missing `Microsoft.AspNetCore`. v0.49.1 removes that product runtime dependency entirely and uses base .NET networking only:

- `TcpListener(IPAddress.Loopback, 0)`;
- exact IPv4 loopback only;
- OS-selected ephemeral port;
- random 256-bit endpoint path token;
- HTTP headers <= 16 KiB;
- decoded JSON-RPC body <= 64 KiB;
- either exact `Content-Length` or exact `Transfer-Encoding: chunked`, never both;
- bounded chunk-size lines and <= 4 KiB trailers;
- exact POST/path/local Host/application-json checks;
- one response then connection close.

Official `ModelContextProtocol 2.2.0` interoperability proved that Streamable HTTP initialization may use chunked transfer. v0.49.1 therefore admits **only a bounded chunked decoder under the same 64 KiB decoded-body ceiling**, rather than treating chunked transport itself as authority expansion.

The runtime product remains compatible with the accepted offline `--no-restore` Workbench update path and no longer requires `Microsoft.AspNetCore.App`. The official MCP client package remains qualification-only and is not a Workbench runtime dependency.

```text
Official Client Interop != Embedded Official Server SDK
CI Runtime Availability != Accepted Host Runtime Guarantee
Chunked Transport != Unbounded Body Authority
Offline Successor Build != Package/Framework Installation Authority
```

## Loopback and secret boundary

The adapter binds only `127.0.0.1`, validates the local Host and secret endpoint path, and never binds `0.0.0.0`, IPv6-any or LAN/public interfaces. The endpoint token is not a replacement for the lease: every content read still passes v0.48 authorization.

v0.48 persists only SHA-256 of the lease bearer. Adapter receipts persist only SHA-256 of the random endpoint token and never bearer plaintext or private read content. Stop clears the Workbench-held bearer reference; this is not a managed-memory zeroization claim.

## Tunnel boundary

The loopback endpoint is not directly reachable by a cloud ChatGPT session. Workbench does not create Secure MCP Tunnel, log into an OpenAI account or modify ChatGPT connector settings.

```text
Local MCP Endpoint
!= Secure MCP Tunnel
!= ChatGPT Account Connection
!= Permission To Read Beyond Lease
```

## Fail-closed behavior

No file content is returned for inactive/missing/expired/revoked/exhausted lease, wrong bearer, out-of-scope role/path, traversal/reparse, byte/call breach, stale SHA, file drift, malformed/oversized HTTP, wrong Host/path/method/content type, unsupported/ambiguous transfer coding, over-limit chunked body/trailers, or caller-injected authority-like tool fields.

The v0.47 manual clipboard relay remains available whenever the direct adapter/tunnel path is unavailable or undesirable.
