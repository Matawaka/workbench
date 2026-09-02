# Workbench v0.49.1 — MCP Runtime Dependency Closure

## Trigger

Real-host v0.49 admission on Windows failed after a fresh active read lease because adapter startup attempted to load `Microsoft.AspNetCore, Version=10.0.0.0`, which was available in CI but not guaranteed by the accepted Workbench host/runtime contract.

This failure is classified as `V049_RUNTIME_DEPENDENCY_GAP`, not a read-lease failure.

## Exact local predecessor

- local failed-v0.49 commit: `9b6b7b5b895ef6364c7c964ecb8d7084d3de6944`
- local predecessor tag: `workbench-v0.49-accepted`
- remote accepted frontier remains: `2b0e3127e3c0284ff569720d85889cec3374f053 / workbench-v0.48-accepted`
- target semantic version: `0.49.1`
- target tag: `workbench-v0.49.1-accepted`

The local v0.49 tag is historical local negative evidence only and must remain absent remotely.

## Stabilization

v0.49.1 removes the product `Microsoft.AspNetCore.App` framework dependency and preserves the same lease-gated MCP read surface on base .NET networking only:

- `TcpListener(IPAddress.Loopback, 0)`;
- IPv4 `127.0.0.1` only;
- OS-selected ephemeral port;
- random 256-bit `/mcp/<token>` path;
- bounded HTTP headers (`<= 16 KiB`);
- bounded request body (`<= 64 KiB`);
- bounded chunked decoding with trailers (`<= 4 KiB`);
- exact Host/path/method/content-type validation;
- one response then connection close;
- one content tool: `read_local_app_chunk`.

Official `ModelContextProtocol 2.2.0` interoperability is qualification evidence only and remains outside the product runtime dependency graph.

## Authority invariants

```text
Runtime Dependency Gap != Lease Failure
CI Dependency Availability != Accepted Host Dependency Guarantee
MCP Request != Filesystem Authority
MCP Adapter Authority <= Active Read Lease Authority
Adapter Startup != Lease Creation
Lease Bearer != MCP Tool Argument
Loopback Listener != Public Listener
Local MCP Endpoint != Automatic Secure Tunnel
Failed Local Tag != Remote Accepted Tag
```

The MCP caller still cannot provide `ApplicationId`, `LeaseId`, bearer or filesystem root. Every content call delegates to the accepted v0.48 `LocalAppReadLeaseV048Service.AuthorizeAndReadAsync`.

## Publication boundary

After real-host PASS, v0.49.1 publication may fast-forward remote `main` from exact accepted v0.48 through the local historical v0.49 parent to v0.49.1. It creates only `workbench-v0.49.1-accepted`; `workbench-v0.49-accepted` must remain absent remotely.

Runtime lease state, bearer plaintext, endpoint-token plaintext, private Apps/AppSources bytes and MCP response data remain outside Git publication.
