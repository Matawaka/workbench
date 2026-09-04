# Matawaka Workbench v0.51.11.1 — Exclusive Local Apps Routing Hotfix

## Real-host defect

v0.51.11 first-boot acceptance passed, but a real `Read session lease + local MCP` action returned the inherited legacy result `OWNED_INDEXED_READ_LEASE_AND_LOCAL_MCP_READY` instead of the v0.51.11 owner→lease transaction result.

The composed configure chain was:

`v0.51.11 -> v0.51.10 -> v0.51.9 -> v0.51.8 -> v0.51.7`

v0.51.8 subscribes `LocalAppsV0518Button_Click`. v0.51.11 removed only `LocalAppsV0517Button_Click`, so the v0.51.8 and v0.51.11 handlers remained subscribed together.

## Hotfix

v0.51.11.1 runs the full inherited configure chain and then performs one final exclusive routing normalization:

- detach inherited `LocalAppsV0518Button_Click`;
- detach inherited v0.51.7 handler defensively;
- detach direct `LocalAppsV05111Button_Click`;
- attach exactly one hotfix wrapper `LocalAppsV051111Button_Click`;
- wrapper emits `local-app.v051111.dispatch exclusive=true; target=v05111` and delegates only to the v0.51.11 handler.

Therefore `ReadSessionLease` is reachable only through the v0.51.11 switch branch and `CreateOwnedReadLeaseAndAutoStartMcpV05111Async`.

## Authority boundary

This is a routing/admission hotfix only:

- no new lease/read/revoke/resume authority;
- no change to canonical v0.48 lease semantics;
- no change to v0.51.5 active index or v0.51.6 fence;
- no change to v0.51.7 MCP ownership;
- no change to v0.51.8 status/recovery;
- no change to v0.51.9 owner evidence continuity;
- no change to v0.51.10 owner-generation transaction;
- no change to v0.51.11 PREPARED_BINDING → LEASE_CREATED → OWNER_BOUND transaction semantics;
- no network/tunnel/publication/catalog/Agent Execute/ActionPermit authority.

## Exact local predecessor

This hotfix is packaged only for:

- tag: `workbench-v0.51.11-accepted`
- commit: `89bb3cc263a9e312df77bef19ee6ab738eb319ca`

Target tag: `workbench-v0.51.11.1-accepted`.
