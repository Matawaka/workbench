# Local App Read Session Leases

Workbench v0.48 adds a bounded local lease layer above the accepted local-app read primitive.

## Operator flow

1. `Local apps` -> choose a registered app.
2. Choose `Read session lease`.
3. Paste one `matawaka.local-app-read-lease-request/v0.48` JSON request.
4. Review the content-free preview: selected app, scopes, TTL, per-read limit, total byte budget and call budget.
5. Explicitly confirm creation.
6. Workbench creates local ignored lease state and copies a one-time grant JSON containing `LeaseId` and the 256-bit bearer to the Windows clipboard.
7. Keep the grant only for the intended future bounded adapter/session.
8. Use `Revoke active read leases` when the session should end early.

Example:

```json
{
  "Schema": "matawaka.local-app-read-lease-request/v0.48",
  "RequestId": "lsr-dev-read-001",
  "ApplicationId": "life-situation-resolver",
  "Scopes": [
    { "Role": "installed", "PathPrefix": "data/state.json" },
    { "Role": "source", "PathPrefix": "web/" }
  ],
  "MaxBytesPerRead": 65536,
  "MaxTotalBytes": 524288,
  "MaxCalls": 8,
  "TtlSeconds": 300
}
```

A path ending with `/` is an explicit existing directory-prefix scope. A path without `/` is an exact existing file scope. Application-root wildcards are refused.

## Limits

Hard v0.48 ceilings:

- scopes: 16;
- read bytes per call: 1,048,576;
- total bytes: 8,388,608;
- calls: 32;
- TTL: 900 seconds.

A request may choose smaller values.

## Bearer handling

Workbench generates 32 random bytes for each lease and returns them as a 64-hex-character bearer in the grant. Persisted lease state contains only SHA-256 of that bearer. Creation and consumption receipts never contain the plaintext bearer.

The bearer is authentication material, not a capability expansion mechanism:

```text
Bearer Possession != Authority Beyond Lease Bounds
```

A valid bearer still cannot escape:

- selected ApplicationId;
- installed/source role;
- normalized file/prefix scopes;
- 1 MiB hard read ceiling;
- lease per-read/total byte limits;
- call count;
- expiry;
- revocation;
- optional expected whole-file SHA-256.

## Future adapter request

The reusable service accepts a future-adapter request shaped as:

```json
{
  "Schema": "matawaka.local-app-lease-read-request/v0.48",
  "RequestId": "read-001",
  "LeaseId": "lease-...",
  "Bearer": "...",
  "ApplicationId": "life-situation-resolver",
  "Role": "installed",
  "RelativePath": "data/state.json",
  "Offset": 0,
  "MaxBytes": 65536,
  "ExpectedFileSha256": "optional exact sha256"
}
```

v0.48 does not itself expose this service over a network. It is a transport-neutral substrate for a later adapter.

## Fail-closed conditions

No content is returned when any of these are observed:

- missing/invalid lease state;
- wrong bearer;
- expired or revoked lease;
- call or byte budget exhausted;
- role/path outside scope;
- unsafe traversal or reparse boundary;
- MaxBytes outside hard or lease limit;
- stale `ExpectedFileSha256`;
- file drift between metadata/hash preview and actual read.

Successful reads update persisted remaining calls/bytes before a response is returned. If state persistence fails, the service does not return the read response.

## Non-effects

v0.48 creates no:

- arbitrary filesystem authority;
- file mutation/write authority;
- process launch authority;
- HTTP listener;
- MCP server;
- tunnel;
- automatic upload/network transport;
- Git/catalog/Agent Execute authority.

The v0.47 manual `Chat read relay` remains the fallback when no direct adapter is in use.
