# Workbench v0.52 — Bounded Artifact Acquisition Primitive

This directory defines the provider-neutral contract surface for bounded artifact acquisition.

It is intentionally **not** KONTUR-specific. KONTUR is one future caller; another local application may supply the same request contract without inheriting KONTUR semantics.

## Core boundary

```text
Artifact identity / handoff
        !=
Acquisition authority
        !=
Network request started
        !=
Bytes complete
        !=
Size verified
        !=
SHA-256 verified
        !=
Artifact use authority
```

A valid `artifact-acquisition-request.v052.schema.json` document is only declarative input. Validation or preview creates no network or filesystem effect and grants no authority.

## Normal corridor

```text
request
  -> PREVIEW (no effect)
  -> explicit one-shot acquisition grant
  -> ACQUISITION_PREPARED
  -> DOWNLOAD_STARTED
  -> BYTES_COMPLETE
  -> SIZE_VERIFIED
  -> SHA256_VERIFIED
  -> verified .partial atomic promotion
  -> ACQUISITION_VERIFIED
```

For a set of multiple artifacts, every member must independently reach exact SHA-256 verification before the set is terminally classified `ACQUISITION_VERIFIED`.

## Authority envelope

The grant binds all of the following before network access:

- exact initial HTTPS URI for every artifact;
- exact destination filename;
- exact expected byte size;
- exact expected SHA-256;
- exact reviewed hostname/path-prefix route rules for redirects;
- fixed existing local destination root;
- total network byte ceiling;
- redirect ceiling;
- timeout;
- TTL;
- one call only.

The bearer plaintext is returned to the immediate caller once and is not persisted by Workbench. Canonical local authority state stores only its SHA-256.

The call budget is consumed **before** network access. A crash or terminal failure therefore does not create implicit retry or range-resume authority.

## Destination boundary

The destination root must already exist and must be outside the Workbench Git repository. Relevant existing path components are rejected fail-closed if they are symlink/junction/reparse points.

A transfer writes to an exact lease-specific `.partial` path. The final path is promoted atomically only after exact size and SHA-256 verification. A different pre-existing final file is never overwritten. An already-existing exact verified final file may be classified/reused without network access.

## Network boundary

The primitive does not expose general HTTP authority:

- HTTPS only;
- no wildcard hosts;
- no automatic redirects;
- every redirect is explicitly counted and route-revalidated;
- no auth header is supplied by the primitive;
- cookies are disabled for its default transport;
- response decompression is disabled;
- streaming stops at the exact byte ceiling.

Provider-specific redirect policies belong in reviewed adapters/handoffs, not in the generic primitive.

## Non-effects

Even a successful `ACQUISITION_VERIFIED` receipt does **not** authorize or perform:

- archive extraction;
- installation;
- script/process execution;
- PATH/environment mutation;
- runtime/model server start;
- benchmark;
- model request/inference;
- game access;
- Git/catalog mutation;
- Agent Execute / ActionPermit;
- Secure MCP Tunnel or general network access.

## KONTUR usage

KONTUR may translate an exact selected-artifact record into this generic request shape, but the adapter must preserve:

```text
Selection != Workbench Acquisition Authority
Verified Bytes != KONTUR Runtime Authority
```

The current prepared KONTUR identities live under `integrations/kontur/`. They remain reference/planning evidence until exact source URIs/routes and a fresh explicit human acquisition decision are separately admitted.

## Qualification

The v0.52 hostile suite exercises exact happy-path acquisition, existing-file reuse, no-overwrite behavior, source/redirect policy refusal, byte/size/hash mismatch, bearer/expiry/timeout boundaries, external-to-Git destination enforcement, one-shot failure behavior, and cross-process destination serialization.

Additional provider adapters must add their own source/redirect qualification without weakening this generic contract.
