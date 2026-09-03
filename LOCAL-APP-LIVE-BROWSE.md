# Local App Live Browse — v0.51

`list_local_app_entries` is a read-only MCP metadata tool layered on the existing v0.48 local-app read lease. It does not create a second filesystem authority model.

## Required active state

The selected application already has:
1. an explicit v0.48 read lease;
2. a running lease-gated local MCP adapter;
3. optionally, a separately authorized Secure MCP Tunnel for ChatGPT reachability.

The MCP runtime fixes `ApplicationId`, `LeaseId` and bearer before any remote request. They are not arguments of the browse tool.

## Directory-prefix authority

Browse is permitted only when the active lease contains a matching directory-prefix scope:

```json
{"Role":"installed","PathPrefix":"data/"}
```

The caller may list `data` and nested directories such as `data/history`, provided they remain inside that prefix. An exact-file lease does not authorize enumeration around that file.

Root scope remains unavailable. A lease cannot contain an empty/`.` root wildcard, so the browse tool cannot enumerate an entire installed/source application unless the human explicitly leases specific top-level directories separately.

## Tool request

```json
{
  "role": "installed",
  "relativeDirectory": "data",
  "startIndex": 0,
  "maxEntries": 64
}
```

Bounds:
- role: exactly `installed` or `source`;
- startIndex: >= 0;
- maxEntries: 1..256;
- no recursion/glob/search argument;
- no absolute path;
- no ApplicationId, LeaseId, bearer or root argument.

## Response metadata

Each immediate child contains only:
- `RelativePath`
- `Kind`: `file` or `directory`
- `FileBytes`: file length or null for a directory

No file bytes, hashes, timestamps, ACLs, extended attributes or target information are disclosed by listing.

Directories have a trailing `/` in `RelativePath` so the caller can request a subsequent nested page explicitly.

## Pagination and accounting

Entries are ordinal-sorted by immediate-child name. The response includes:
- total immediate-child count;
- page start;
- returned count;
- nullable next start index.

The serialized UTF-8 entry array is the disclosure unit charged to the existing lease byte budget. A successful list consumes one lease call. The metadata charge must fit both the lease `MaxBytesPerRead` and the fixed 64 KiB list ceiling, as well as the remaining lease bytes.

This makes repeated discovery observable and finite rather than a free side channel around the read budget.

## Fail-closed boundaries

The operation refuses:
- expired/revoked/exhausted lease;
- wrong bearer;
- role mismatch;
- directory outside a directory-prefix scope;
- listing authorized only by an exact-file scope;
- application-root listing;
- traversal escaping the leased root;
- missing directory;
- reparse/junction/symlink directory or child;
- start index beyond current entry count;
- page metadata exceeding byte ceilings.

A failure does not consume a call or byte budget because lease state is mutated only after the bounded listing has been fully validated.

## Relationship to file read

Browse metadata does not imply permission to read a file outside the same active lease. `read_local_app_chunk` independently rechecks role/path scope, bearer, TTL, call/byte budgets and optional expected whole-file SHA-256.

Typical ChatGPT flow:

```text
list_local_app_entries(data)
    -> choose data/state.json
read_local_app_chunk(data/state.json, offset=0, maxBytes=...)
```

Both calls consume the same lease state.
