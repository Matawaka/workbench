# Workbench v0.50.2 — Plain MCP OAuth Discovery Compatibility Closure

Predecessor (local failed real-host frontier):
- `1b3c5aa44e2bb302764b044b3b3ac00de14a5994`
- local tag `workbench-v0.50.1-accepted`

Published remote base remains:
- `1e0453ccf047bd948e76b577c2395a7d0009ff7a`
- `workbench-v0.49.1-accepted`

## Real-host evidence that triggered this stabilization

v0.50.1 did its job: instead of collapsing tunnel admission into a generic timeout, it exposed the exact bounded/redacted readiness reason.

Observed on the Windows host:
- tunnel-client process remained live;
- `/healthz = 200 live`;
- `/readyz = 503`;
- readiness reason: OAuth protected-resource discovery tried the local no-auth MCP target and received malformed/non-JSON metadata rather than an explicit no-metadata result.

The local MCP is intentionally not OAuth-protected. File-content authority is the already-accepted v0.48 read lease, bound into the v0.49.1 loopback MCP session. Adding an authorization server or DCR would be an authority expansion and is not required.

OpenAI tunnel-client's plain HTTP/no-auth profile treats 404 from all Protected Resource Metadata candidates as an optional discovery result; malformed metadata is readiness-failing. v0.50.2 therefore represents the actual no-auth semantics explicitly instead of inventing OAuth.

## Change

A bounded loopback-only compatibility facade is inserted only between the already-active lease-gated MCP endpoint and the separately authorized Secure MCP Tunnel session.

The facade:
- listens only on `127.0.0.1` on an ephemeral port;
- uses its own random 256-bit secret MCP path;
- returns HTTP 404 for exactly:
  - `/.well-known/oauth-protected-resource`
  - `/.well-known/oauth-protected-resource` + the exact facade MCP path;
- advertises no OAuth metadata;
- injects no `WWW-Authenticate` header;
- forwards only POST traffic to the exact existing loopback MCP endpoint;
- does not forward `Authorization` or `Cookie` headers upstream;
- has no filesystem access and no lease/bearer authority;
- is bounded by the same read-lease expiry;
- stops with tunnel stop/failure/Workbench close.

## Preserved boundaries

- top-level operator surface remains exactly four actions;
- v0.48 read lease remains the only file-content authority;
- v0.49.1 MCP tool surface remains `read_local_app_chunk` only;
- v0.50.1 bounded/redacted `/healthz` + `/readyz` admission remains unchanged;
- official OpenAI tunnel-client remains external fixed-path runtime-only tooling;
- no tunnel creation/deletion/admin authority;
- no automatic ChatGPT configuration;
- no public inbound listener;
- no application/source mutation;
- no OAuth authorization server or DCR implementation.

## Publication rule

The failed local tags:
- `workbench-v0.50-accepted`
- `workbench-v0.50.1-accepted`

must remain absent remotely. Only a successful real-host v0.50.2 chain may publish `workbench-v0.50.2-accepted`.
