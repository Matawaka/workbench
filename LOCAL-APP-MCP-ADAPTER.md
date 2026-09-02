# Local App Lease-Gated Read-Only MCP Adapter

Workbench v0.49 adds a local MCP transport adapter above the accepted v0.48 read-lease authority.

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

The v0.49 content surface intentionally contains one tool:

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

The MCP caller does **not** supply:

- `ApplicationId`;
- `LeaseId`;
- lease bearer;
- filesystem root.

Those values are fixed in the in-process adapter session created from the human-approved v0.48 grant.

## Read authority

Each MCP tool invocation is translated to `matawaka.local-app-lease-read-request/v0.48` and sent to `LocalAppReadLeaseV048Service.AuthorizeAndReadAsync`.

Therefore MCP cannot bypass:

- selected registered `ApplicationId`;
- exact `installed|source` role;
- exact-file/directory-prefix lease scopes;
- path normalization and reparse refusal;
- maximum bytes per read;
- remaining total byte budget;
- remaining call budget;
- lease expiry;
- revocation;
- optional expected whole-file SHA-256.

```text
MCP Request != Filesystem Authority
MCP Adapter Authority <= Active Read Lease Authority
Bearer Possession != Authority Beyond Lease Bounds
```

## Runtime protocol and official-client qualification

The accepted Workbench updater builds the successor with `--no-restore` and creates no package-download authority. A new runtime NuGet dependency would therefore make successful update depend on unproven local NuGet cache state or would require broadening the updater's network boundary.

v0.49 deliberately avoids that authority expansion. The product runtime implements a small allowlisted MCP JSON-RPC / Streamable HTTP subset using the installed `Microsoft.AspNetCore.App` shared framework. It accepts only the protocol methods needed for discovery/legacy initialization, ping, `tools/list`, and `tools/call`, and it exposes only the single read tool above.

The runtime product remains compatible with the accepted offline `--no-restore` Workbench update path.

Interoperability is a separate qualification boundary: Windows CI must connect with the official C# `ModelContextProtocol 2.2.0` client, list the tool and successfully perform/refuse lease-gated calls. That qualification proves client interoperability for the tested protocol surface; it does not claim that Workbench embeds the official MCP server SDK.

```text
Official Client Interop != Embedded Official Server SDK
Offline Successor Build != Package Download Authority
```

## Loopback security boundary

v0.49 binds Kestrel only to IPv4 loopback. Startup refuses a runtime that reports any additional listener address. A random 256-bit endpoint path token makes the URL unguessable by ordinary local callers, and host-header middleware accepts only `127.0.0.1`/`localhost`.

The endpoint token is not a replacement for the lease. Even a caller that reaches the endpoint can perform only the tool calls allowed by the fixed active lease.

```text
Loopback Listener != Public Listener
Endpoint Token != Lease Authority
```

## Secret persistence

v0.48 persists only SHA-256 of the lease bearer. v0.49 start/stop receipts persist only SHA-256 of the random endpoint token and never the bearer plaintext or private read content.

The adapter holds the bearer in process memory while active because it must authenticate each delegated v0.48 read. Stop clears the Workbench-held string reference. This is reference clearing, not a cryptographic managed-memory-erasure guarantee.

## Tunnel boundary

The loopback endpoint is not directly reachable by a cloud ChatGPT session. v0.49 does not create Secure MCP Tunnel, log into an OpenAI account or modify ChatGPT connector settings.

Any future tunnel step is a separate authority boundary:

```text
Local MCP Endpoint
!= Secure MCP Tunnel
!= ChatGPT Account Connection
!= Permission To Read Beyond Lease
```

The account/product UI must be checked at activation time; code readiness is not evidence that a particular account currently has the necessary connector/tunnel capability.

## Fail-closed behavior

MCP read returns no file contents when the underlying lease service observes:

- inactive/missing lease;
- wrong bearer state;
- expired/revoked/exhausted lease;
- role/path outside scope;
- traversal or reparse boundary;
- per-read/total byte limit breach;
- expected SHA mismatch;
- file drift during the bounded read;
- a caller tries to inject authority-like fields not present in the allowlisted tool arguments.

## Non-effects

v0.49 does not create:

- application/source write authority;
- arbitrary filesystem authority;
- application process execution authority;
- LAN/public listener;
- automatic Secure MCP Tunnel;
- automatic upload to ChatGPT;
- runtime MCP package-download authority;
- Git/catalog/Agent Execute authority.

The v0.47 manual clipboard relay remains available whenever the direct adapter/tunnel path is unavailable or undesirable.
