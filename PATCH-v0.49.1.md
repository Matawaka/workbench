# Workbench v0.49.1 — MCP Runtime Dependency Closure

## Trigger

Real-host v0.49 admission reached a fresh active v0.48 read lease, then failed before listener creation because the host did not contain `Microsoft.AspNetCore, Version=10.0.0.0`.

Observed classification:

`V049_RUNTIME_DEPENDENCY_GAP`

The failure was not a lease/authentication failure. It proved that the first v0.49 transport implementation accidentally relied on a shared runtime available in CI but not guaranteed by the accepted Workbench update/runtime contract.

## Stabilization

v0.49.1 removes the `Microsoft.AspNetCore.App` framework reference and replaces Kestrel/WebApplication with a bounded base-.NET `TcpListener` HTTP transport.

Preserved properties:

- exact `127.0.0.1` listener only;
- OS-selected ephemeral port;
- random 256-bit secret endpoint path;
- one read-only MCP tool `read_local_app_chunk`;
- caller cannot select ApplicationId/LeaseId/bearer/root;
- every read delegates to accepted v0.48 `AuthorizeAndReadAsync`;
- expected SHA, scope, TTL, call and byte ceilings remain fail-closed;
- stop/revoke remain independent;
- no Secure MCP Tunnel/account/public endpoint action.

Additional transport bounds:

- HTTP headers <= 16 KiB;
- request body <= 64 KiB;
- exact Content-Length required;
- Transfer-Encoding/chunked refused;
- exact POST + endpoint path + local Host + application/json required;
- one request/response per connection, then close.

## Qualification requirement

Before another real-host attempt:

1. Release build 0 warnings/errors.
2. Product project and publish/deps contain no `Microsoft.AspNetCore*` runtime dependency.
3. A restored v0.49 source tree plus only v0.49.1 stabilization changes builds with `--no-restore`.
4. Official `ModelContextProtocol 2.2.0` client still passes initialize/tools-list/tools-call.
5. Installed/source read success and out-of-scope/stale/caller-authority/revoked negatives pass.
6. Loopback endpoint stops cleanly.

## Version/predecessor boundary

The user's failed-but-locally-checkpointed v0.49 was never published. Final v0.49.1 package generation must bind the exact local `workbench-v0.49-accepted` commit SHA observed on that host. Until that SHA is captured, this stabilization branch is qualification source only and is not an installable successor package.

```text
Runtime Dependency Gap != Lease Failure
CI Dependency Availability != Host Dependency Guarantee
Stabilization != Publication
Offline Build != Framework Installation Authority
```
