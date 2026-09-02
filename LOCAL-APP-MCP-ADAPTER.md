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

The content surface intentionally contains one tool:

`read_local_app_chunk`

Arguments exposed to the MCP caller:

```json
{
  "role": "installed|source",
  "relativePath": "data/state.json",
  "offset": 0,
  "maxBytes": 65536,
  "expectedFileSha256": "optional exact SHA-256"
}
```

The MCP caller does **not** supply `ApplicationId`, `LeaseId`, lease bearer or filesystem root. Those values are fixed in the in-process adapter session created from the human-approved v0.48 grant.

## Read authority

Each MCP tool invocation is translated to `matawaka.local-app-lease-read-request/v0.48` and sent to `LocalAppReadLeaseV048Service.AuthorizeAndReadAsync`.

Therefore MCP cannot bypass selected registered `ApplicationId`, `installed|source` role, exact-file/directory-prefix lease scopes, path/reparse boundaries, per-read/total byte ceilings, call budget, expiry, revocation or optional expected SHA-256.

```text
MCP Request != Filesystem Authority
MCP Adapter Authority <= Active Read Lease Authority
Bearer Possession != Authority Beyond Lease Bounds
```

## Runtime protocol and dependency closure

The accepted Workbench updater builds the successor with `--no-restore` and creates no package-download or shared-framework-install authority. CI availability of an assembly is not evidence that the accepted user host has that runtime.

The initial v0.49 Kestrel implementation therefore failed real-host admission with missing `Microsoft.AspNetCore`. v0.49.1 removes that product runtime dependency entirely.

The stabilized adapter uses only base .NET networking:

- `TcpListener(IPAddress.Loopback, 0)`;
- exact IPv4 loopback only;
- OS-selected ephemeral port;
- one random 256-bit endpoint path token;
- bounded HTTP/1.1 header parser (`<= 16 KiB`);
- exact `Content-Length`, body `<= 64 KiB`;
- no chunked/transfer-encoding input;
- exact POST/path/Host/content-type checks;
- one response then connection close.

The protocol surface remains a small allowlisted MCP JSON-RPC / Streamable HTTP subset for initialization, ping, `tools/list`, and `tools/call` with the single read tool.

The runtime product remains compatible with the accepted offline `--no-restore` Workbench update path and no longer requires `Microsoft.AspNetCore.App`.

Interoperability remains a separate qualification boundary: Windows CI connects with the official C# `ModelContextProtocol 2.2.0` client, lists the tool and performs/refuses lease-gated calls. The official client package is qualification-only and is not a Workbench runtime dependency.

```text
Official Client Interop != Embedded Official Server SDK
CI Runtime Availability != Accepted Host Runtime Guarantee
Offline Successor Build != Package/Framework Installation Authority
```

## Loopback security boundary

The adapter binds only `127.0.0.1`, validates the exact local Host header and endpoint path, and never binds `0.0.0.0`, IPv6-any or LAN/public interfaces. The random endpoint token is not a replacement for the lease: every content read still passes the v0.48 lease gate.

```text
Loopback Listener != Public Listener
Endpoint Token != Lease Authority
```

## Secret persistence

v0.48 persists only SHA-256 of the lease bearer. Adapter start/stop receipts persist only SHA-256 of the random endpoint token and never bearer plaintext or private read content. While active, the bearer remains only in the Workbench process because delegated v0.48 reads require it. Stop clears the Workbench-held string reference; this is reference clearing, not a managed-memory zeroization claim.

## Tunnel boundary

The loopback endpoint is not directly reachable by a cloud ChatGPT session. Workbench does not create Secure MCP Tunnel, log into an OpenAI account or modify ChatGPT connector settings.

```text
Local MCP Endpoint
!= Secure MCP Tunnel
!= ChatGPT Account Connection
!= Permission To Read Beyond Lease
```

## Fail-closed behavior

No file content is returned for inactive/missing/expired/revoked/exhausted lease, wrong bearer state, out-of-scope role/path, traversal/reparse, byte-budget breach, stale expected SHA, file drift, malformed/oversized HTTP, wrong Host/path/method/content type, chunked transfer, or caller-injected authority-like tool fields.

## Non-effects

The stabilized adapter creates no application/source write authority, arbitrary filesystem authority, app process execution authority, LAN/public listener, automatic Secure MCP Tunnel, automatic upload to ChatGPT, runtime MCP/AspNetCore package dependency, Git/catalog/Agent Execute authority, or update restore/network authority.

The v0.47 manual clipboard relay remains available whenever the direct adapter/tunnel path is unavailable or undesirable.
