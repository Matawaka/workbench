# Workbench v0.51.6 — Cross-Process Active-Index Fence + Snapshot Coherence

## Goal
Make verified active-index authority observation/mutation coherent when more than one Workbench process is open against the same workspace.

## Core invariant

`Process-local serialization != Workspace authority serialization`

v0.51.5 keeps canonical v0.48 lease state as the source of truth and uses a verified derived active index, but its `SemaphoreSlim` is process-local. v0.51.6 adds an app-scoped cross-process fence without changing canonical or index schemas.

## Fence
For each `(WorkspaceRoot, ApplicationId)` Workbench uses an exclusive file handle under:

`.workbench/active-index-fence-v0516/<ApplicationId>/active-index-v0.51.6.lock`

Ownership is the open `FileStream` with `FileShare.None`, not file contents. The persistent lock file is empty and grants no authority.

Properties:
- app-scoped: different ApplicationIds do not block each other;
- await-safe: file-handle ownership is not thread-affine;
- process termination releases ownership automatically;
- bounded acquisition timeout (default 3000 ms) returns `ACTIVE_INDEX_FENCE_BUSY`;
- lock root/app/file reparse points are rejected fail-closed;
- no bearer plaintext/hash, scope, endpoint, LeaseId or authority material is stored in the lock file.

## Lifecycle integration
The existing v0.51.5 lifecycle bridge is fenced around:
- indexed lease creation;
- exact indexed revoke;
- revoke-all + reconciliation recovery;
- index readiness observation;
- bounded reconciliation;
- fast live-authority status;
- exact indexed live-lease observation.

Canonical v0.48/v0.51.2 writers and v0.51.5 index schemas are unchanged.

## Coherent fast status
`Read Session Status` on v0.51.6:
1. acquires the app-scoped cross-process fence;
2. requires verified index ready + no dirty marker;
3. runs v0.51.5 exact-canonical candidate revalidation;
4. permits v0.51.5 lazy derived-index prune under the same fence;
5. re-reads readiness;
6. requires dirty still absent and post-observation `IndexRevision` equal to the returned snapshot revision;
7. only then returns `COHERENT_VERIFIED_ACTIVE_INDEX_STATUS`.

The receipt explicitly exposes `CrossProcessFenceAcquired`, fence wait time, revision before/after, dirty absence before/after, and `SnapshotCoherent`.

## Crash semantics
OS fence ownership disappearing does not erase v0.51.5 crash evidence. If a process dies after the durable dirty marker is written, the next process may acquire the fence but index use remains blocked until explicit bounded reconciliation.

## Safety / non-effects
- canonical lease-state authority remains unchanged;
- active index remains derived only;
- no historical evidence deletion/compaction;
- no automatic lease creation/renewal/revocation;
- no read/list budget consumption by the fence/status;
- no application/source mutation;
- no network, MCP tunnel, publication, catalog mutation, Agent Execute or ActionPermit authority;
- public remote Workbench publication remains deferred.
