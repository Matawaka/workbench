# Workbench v0.50 — Secure MCP Tunnel Handoff

## Accepted predecessor

- commit: `1e0453ccf047bd948e76b577c2395a7d0009ff7a`
- tag: `workbench-v0.49.1-accepted`

## Purpose

v0.50 connects the accepted v0.49.1 lease-gated IPv4-loopback MCP adapter to the supported OpenAI Secure MCP Tunnel runtime **without turning transport reachability into filesystem authority**.

The layer deliberately does not implement a tunnel protocol itself. It launches a separately provisioned official OpenAI `tunnel-client-runtime` binary from one fixed Workbench Tools path and binds that child process to the already-active local MCP endpoint.

## Fixed external tool boundary

Expected path:

`<WorkspaceRoot>/Tools/OpenAI/tunnel-client/tunnel-client-runtime.exe`

v0.50 is pinned to official tunnel-client runtime semantic version `0.0.14`.

Workbench:

- executes only the fixed path;
- refuses reparse-point tool paths;
- runs `--version` without tunnel credentials;
- records exact executable SHA-256 + size + reported version;
- does **not** download/install/update the tool automatically;
- does **not** commit the binary into the Workbench repository.

The operator must obtain the matching Windows amd64 runtime from the official `openai/tunnel-client` release and verify the downloaded release archive against OpenAI's published `SHA256SUMS.txt` before extracting the runtime executable into the fixed Tools path.

```text
Observed Fixed Binary != Automatic Vendor-Provenance Claim
Manual Verified Provisioning != Workbench Download Authority
```

## Explicit session flow

```text
Registered App
-> bounded v0.48 read lease
-> v0.49.1 loopback MCP adapter
-> existing OpenAI tunnel_id
-> session-only runtime API key
-> explicit Secure MCP Tunnel confirmation
-> fixed tunnel-client-runtime child
-> loopback /readyz PASS
-> separate ChatGPT developer/app connection to same tunnel_id
-> bounded read_local_app_chunk calls
-> Stop Secure MCP Tunnel
-> Stop local MCP adapter
-> Revoke read lease
```

Tunnel startup is refused without an active local MCP adapter and a non-expired lease binding.

## Credential / endpoint handling

The runtime API key is entered into a WPF `PasswordBox` and is never serialized into Workbench settings, receipts or Git.

The secret local MCP endpoint is also not serialized by v0.50. Both values are passed to the child process through environment variables:

- `CONTROL_PLANE_API_KEY`
- `CONTROL_PLANE_TUNNEL_ID`
- `MCP_SERVER_URL`

The child command line contains no runtime API key and no secret local MCP URL. After `Process.Start`, Workbench clears those values from its parent-side `ProcessStartInfo` environment dictionary. This is reference/value clearing at the parent object boundary, not a claim of operating-system or managed-memory cryptographic erasure.

The fixed child arguments are limited to:

- `run`
- `--control-plane.api-key env:CONTROL_PLANE_API_KEY`
- `--health.listen-addr 127.0.0.1:0`
- `--health.url-file <bounded ignored runtime-state path>`

No `admin`, `init`, tunnel CRUD, profile creation, shell command or arbitrary child arguments are admitted.

## Readiness

`Process.Start()` is not success.

Workbench waits for the official tunnel-client health URL file, requires an exact `http://127.0.0.1:<port>` health base, and polls `/readyz` until success or a bounded timeout. A child exit or timeout fails closed and terminates that exact child process.

```text
Tunnel Process Running != Tunnel Ready
Tunnel Ready != ChatGPT Connector Enabled
```

## Lifetime

The tunnel child lifetime is bounded to the current read lease. An in-process expiry monitor stops the exact Workbench-started tunnel-client child at lease expiry even if the operator does not press Stop.

Manual `Stop OpenAI Secure MCP Tunnel` terminates only that exact Workbench child and does not silently revoke the lease or stop the MCP adapter. Those remain separately observable operations.

## ChatGPT boundary

Workbench does not log into ChatGPT and does not modify ChatGPT developer/app settings. After tunnel readiness the same non-secret `tunnel_id` is copied to the clipboard; selecting `Connection: Tunnel` / the matching tunnel in ChatGPT remains a separate human decision.

The cloud caller still sees only the existing read-only MCP tool. `ApplicationId`, `LeaseId`, bearer and filesystem root remain fixed by the local adapter session rather than caller arguments.

```text
Secure MCP Tunnel != Public MCP Exposure
Tunnel Reachability != Read Authority
Transport Authority != Filesystem Authority
MCP Adapter Authority <= Active Read Lease Authority
```

## Non-effects

v0.50 creates no tunnel CRUD/admin authority, no automatic runtime download, no public inbound listener, no application/source write authority, no arbitrary process execution, no automatic ChatGPT connection, no read-lease renewal/scope widening, no Agent Execute/ActionPermit authority, and no Stable Core/interface-registry promotion.
