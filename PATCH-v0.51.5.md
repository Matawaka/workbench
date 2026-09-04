# Workbench v0.51.5 — Verified Active Lease Index

## Goal
Bound **live read-authority discovery** independently of accumulated historical lease-state count while preserving canonical per-lease evidence.

## Core invariant

`Active Index Candidate != Canonical Lease Authority`

Canonical authority remains each exact v0.48 state file under:

`.workbench/read-leases/<ApplicationId>/lease-*.json`

The derived index lives separately under:

`.workbench/read-lease-index-v0515/<ApplicationId>/active-index-v0.51.5.json`

and stores only `LeaseId + last verified StateRevision`.

It stores no bearer plaintext, no persisted bearer hash, and no scope copy used as authority truth.

## Dirty / reconciliation boundary
Operations that can add or explicitly remove live authority through the v0.51.5 UI use:

`Verified index ready -> durable dirty marker -> canonical operation -> exact canonical verification -> index commit -> dirty clear`

If any step fails after the dirty marker, the marker remains. Fast index status then refuses with `ACTIVE_INDEX_RECONCILIATION_REQUIRED` until an explicit bounded reconciliation is approved.

## Migration
Existing v0.51.4 workspaces have canonical lease evidence but no index. First v0.51.5 index-dependent action prompts for one bounded reconciliation:

- maximum 4096 canonical lease-state files;
- canonical state is read only as Workbench-owned control metadata;
- no canonical state is modified/deleted/compacted;
- no bearer plaintext/hash is disclosed or copied into index/receipt;
- reconciliation records a metadata root that excludes bearer material.

## Status split
- **Read Session Status — verified live authority (fast)**: reads only indexed candidate canonical states; does not enumerate historical lease files.
- **Read Session History Page — bounded canonical evidence scan**: explicit historical path inherited from v0.51.4; live authority is intentionally not presented there.

## Lazy stale-candidate pruning
Read/list budget consumption and natural expiry can only reduce authority. They do not need index authority writes. Fast status revalidates indexed candidates against canonical state and removes expired/revoked/exhausted candidates from the derived index only. Canonical evidence remains unchanged.

## Exact closure
Normal bound-session and orphan-session closure use the existing exact v0.51.2 canonical revoke primitive wrapped by the v0.51.5 dirty/index commit protocol. Sibling leases and historical evidence are not enumerated or revoked by exact closure.

## Safety
- canonical evidence remains source of truth;
- dirty marker prevents use of a partially synchronized derived index;
- live hard ceiling remains 32; overflow returns `LIVE_AUTHORITY_OVERFLOW` without a partial authority list;
- no automatic lease creation/renewal/revocation/retry;
- no bearer plaintext/hash storage in index;
- no network, Secure MCP Tunnel, publication, catalog mutation, Agent Execute or ActionPermit authority;
- public remote Workbench publication remains deferred.