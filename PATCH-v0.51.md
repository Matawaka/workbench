# Workbench v0.51 — Lease-Gated Live App Browse Surface

Accepted predecessor:
- `632ddbb73e8d70b485f02d21f772674d429adf8c`
- `workbench-v0.50.2-accepted`

## Motivation

v0.50.2 proved a complete private-local read path:

```text
ChatGPT plugin
  -> OpenAI Secure MCP Tunnel
  -> no-auth discovery compatibility facade
  -> lease-gated MCP
  -> v0.48 read lease
  -> Apps / AppSources
```

The real-host round-trip read `life-situation-resolver/data/state.json` without exposing the application publicly and without making ApplicationId, LeaseId, bearer or filesystem root caller-selectable.

The remaining friction is discovery. A chat that does not already know a current relative path still needs a previously exported snapshot or a human to tell it which file exists. v0.51 adds bounded live discovery while keeping directory visibility subordinate to the same explicit lease.

## New tool

`list_local_app_entries`

Caller inputs are only:
- `role`: `installed` or `source`;
- `relativeDirectory`;
- `startIndex`;
- `maxEntries` (1..256).

The runtime-fixed ApplicationId, LeaseId and bearer remain outside tool arguments.

## Authority boundary

Listing is authorized only by an existing v0.48 **directory-prefix** scope such as:

```text
installed:data/
source:web/
```

An exact-file scope such as `installed:data/state.json` does **not** authorize listing `data/` or siblings.

Application-root listing remains refused because v0.48 already refuses root wildcard scopes. There is no recursive flag, glob, arbitrary search or arbitrary root.

## Disclosure

One list call returns only immediate children with:
- relative path;
- `file` or `directory` kind;
- file size for files.

It does not return file contents, SHA-256, timestamps, ACLs or other metadata. Reparse/junction/symlink children fail closed rather than becoming traversal surfaces.

Pagination is deterministic over ordinal-sorted immediate-child names. A page has at most 256 entries.

## Lease accounting

A successful list call consumes:
- exactly one existing lease call; and
- the UTF-8 byte length of the serialized returned entry array from the same `RemainingBytes` budget.

Per-call metadata disclosure is bounded by both:
- the lease `MaxBytesPerRead`; and
- the fixed v0.51 64 KiB metadata ceiling.

Accounting happens under the same atomic lease state gate used by `read_local_app_chunk`.

## Preserved behavior

`read_local_app_chunk` is unchanged. `tools/list` now exposes exactly two read-only tools:
1. `read_local_app_chunk`
2. `list_local_app_entries`

v0.50.2 protected-resource discovery 404 behavior, v0.50.1 tunnel readiness diagnostics, external OpenAI tunnel-client handling, the four-button operator surface and explicit Stop Tunnel -> Stop MCP -> Revoke sequence remain unchanged.

## Real-host admission before publication

```text
fresh installed:data/ lease
-> start local MCP
-> start Secure MCP Tunnel
-> ChatGPT refresh/rescan existing Matawaka LSR Read Bridge tool surface if required
-> list_local_app_entries(role=installed, relativeDirectory=data)
-> choose one returned file
-> read_local_app_chunk on that file
-> Stop Tunnel
-> Stop MCP
-> Revoke lease
-> Publish accepted
-> Lifecycle receipt
```

## Invariants

- `Directory Visibility <= Explicit Directory-Prefix Lease Scope`
- `Exact File Read Authority != Sibling Enumeration Authority`
- `List Metadata != File Contents`
- `Browse Authority != Root Authority`
- `Browse Tool != Recursive Search`
- `Tunnel Reachability != Read/Browse Authority`
- `Transport Authority != Filesystem Authority`
