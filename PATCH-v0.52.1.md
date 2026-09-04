# Workbench v0.52.1 — Real-host HTTPS Diagnostics Closure

v0.52.1 is a narrow stabilization successor over the locally accepted v0.52 frontier.

## Trigger

The first immutable 178-byte real-host smoke request reached the v0.52 network boundary but failed with a `HttpRequestException` before Workbench observed an HTTP response. v0.52 correctly failed terminally and created no retry/resume authority, but its operator evidence collapsed DNS/TLS/proxy/connect failures into the generic `NETWORK_FAILED` label.

## Exact local predecessor

```text
workbench-v0.52-accepted
87f98f33bde5b9c2e5de92855a62c3fc12d8fe9f
```

Target:

```text
workbench-v0.52.1-accepted
```

## Added diagnostic boundary

`NetworkFailureDiagnosticsV0521` observes only bounded metadata already present on a caught `HttpRequestException`:

- `HttpRequestError` enum name;
- safe nested `SocketError` enum name when available;
- immediate inner exception type name;
- derived stable transport classification.

Representative classifications include DNS, TLS, proxy tunnel, connection refused, network unreachable, connect timeout, protocol/version negotiation and generic connection failure.

It deliberately does **not** persist:

- raw inner exception messages;
- request headers;
- proxy credentials;
- acquisition bearer plaintext;
- cookies;
- response bodies.

## Preserved v0.52 authority corridor

```text
Preview
-> explicit confirmation
-> one-shot Grant
-> authority consumed before network
-> DOWNLOAD_STARTED
-> BYTES_COMPLETE
-> SIZE_VERIFIED
-> SHA256_VERIFIED
-> atomic promotion
-> ACQUISITION_VERIFIED
```

v0.52.1 does not change the v0.52 request schema, lease schema, source/redirect policy, byte ceilings, timeout/TTL, destination/reparse rules, verification rules or terminal no-retry behavior.

## Non-effects

A diagnostic receipt is evidence only. It grants no:

- retry/resume authority;
- general network/browser authority;
- extraction/install authority;
- process/runtime authority;
- benchmark/model request/game authority;
- Git/catalog/Agent Execute/ActionPermit authority;
- KONTUR LM1/LM3-A acquisition authority;
- publication authority.

## Admission gate

After local v0.52.1 acceptance, repeat the exact 178-byte immutable real-host smoke. Publication and real KONTUR artifact acquisition remain deferred until the transport frontier is resolved.
