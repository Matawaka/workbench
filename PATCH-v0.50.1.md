# Workbench v0.50.1 — Secure MCP Tunnel Readiness Diagnostics Closure

## Trigger

Real-host v0.50 admission failed twice after the already-proven lease and loopback MCP boundaries had started successfully. In both attempts the official OpenAI tunnel-client child was launched but Workbench collapsed all non-success `/readyz` states into one generic timeout.

The second attempt occurred after sufficient tunnel/role propagation time. v0.50 is therefore retained as local negative real-host evidence rather than published as an accepted remote frontier.

## Exact local predecessor

- local failed-v0.50 commit: `bd7b0c4cb69deaee2f575961b9f277e7168485c3`
- local tag: `workbench-v0.50-accepted`
- remote accepted base remains: `1e0453ccf047bd948e76b577c2395a7d0009ff7a / workbench-v0.49.1-accepted`
- target: `workbench-v0.50.1-accepted`

## Change

v0.50.1 does not widen tunnel or filesystem authority. It replaces the generic tunnel-readiness observation with a bounded diagnostic corridor:

1. child process must remain the exact fixed `tunnel-client-runtime` observed by version and SHA-256;
2. health URL must come only from the child-created bounded URL file and resolve to exact IPv4 loopback HTTP;
3. `/healthz` and `/readyz` are observed separately;
4. `/healthz` success is only liveness and never counts as tunnel readiness;
5. `/readyz` success remains mandatory before Workbench reports tunnel ready;
6. the observation window is at most 90 seconds and is shortened to the current read-lease expiry when necessary;
7. response bodies are read with a 4096-byte ceiling and persisted with a 512-character ceiling;
8. runtime credential and secret local MCP endpoint are redacted before any diagnostic persistence;
9. non-success readiness kills the exact Workbench-started tunnel-client child before refusal;
10. failure evidence is written only under ignored local `Workbench/artifacts/secure-mcp-tunnel` state.

## Preserved authority boundary

```text
Readiness Observation != Readiness Authority
Healthz Live != Tunnel Ready
Readyz Reason != Secret Disclosure
Tunnel Reachability != Read Authority
MCP Adapter Authority <= Active Read Lease Authority
Transport Authority != Filesystem Authority
Failed Local Checkpoint != Published Accepted Frontier
```

The existing v0.49.1 MCP adapter remains the content gate. Tunnel startup cannot create, renew or widen a read lease, and ChatGPT-side tunnel selection remains a separate human product action.

## Publication rule

`workbench-v0.50-accepted` must remain absent remotely. After successful v0.50.1 real-host proof, fixed publication may fast-forward remote `main` from exact accepted v0.49.1 to the accepted v0.50.1 HEAD; the failed v0.50 commit may then exist only as untagged historical ancestry.

Required real-host sequence before publication:

```text
fresh read lease
-> local MCP adapter
-> Secure MCP Tunnel
-> /readyz success
-> ChatGPT read round-trip
-> Tunnel Stop
-> MCP Stop
-> Lease Revoke
-> Publish accepted
-> Lifecycle receipt
```

No runtime credential, lease bearer, private app bytes, local MCP endpoint, readiness runtime state or external tunnel-client binary enters Workbench Git publication.
