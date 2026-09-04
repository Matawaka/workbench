# Workbench v0.52 — Bounded Artifact Acquisition Lease

v0.52 adds a provider-neutral reusable primitive for acquiring an exact reviewed artifact set under one explicit, one-shot, fail-closed authority.

## Semantic boundary

```text
Artifact Selected
!= Request Validated
!= Acquisition Authorized
!= Download Started
!= Bytes Complete
!= Size Verified
!= SHA256 Verified
!= Extracted / Installed / Executed / Runtime Ready
```

A KONTUR handoff is one possible caller input. The primitive itself contains no KONTUR-specific model, runtime, benchmark, inference or game behavior.

## Authority corridor

```text
exact JSON request
  -> Preview (no network / no write / no authority)
  -> explicit human confirmation
  -> one-shot acquisition Grant
  -> ACQUISITION_PREPARED
  -> DOWNLOAD_STARTED
  -> BYTES_COMPLETE
  -> SIZE_VERIFIED
  -> SHA256_VERIFIED
  -> atomic final promotion
  -> ACQUISITION_VERIFIED
```

The call budget is consumed before network access. Failure or crash never silently retries or resumes the transfer.

## Transport bounds

- credential-free HTTPS only;
- exact reviewed initial URL and allowlisted host/path-prefix routes;
- redirects disabled in HttpClient and followed only by explicit revalidation;
- bounded redirect count;
- bounded total network bytes and exact per-artifact expected size;
- bounded timeout and TTL;
- no cookies or automatic decompression;
- no general browser/network authority.

## Filesystem bounds

- fixed absolute destination outside the Workbench Git root;
- existing destination required;
- symlink/junction/reparse paths fail closed;
- download goes to non-authoritative `.partial` evidence;
- final path is promoted only after exact size + SHA-256 match;
- an existing different final file is never overwritten;
- an existing exact verified file may be reused without network access;
- same-destination concurrent acquisitions are serialized/refused.

## Explicit non-effects

v0.52 grants no authority for:

- archive extraction;
- installation;
- script or process execution;
- PATH/environment mutation;
- MCP/runtime/model start;
- benchmark;
- model request;
- game access;
- Git/catalog mutation;
- Agent Execute or ActionPermit;
- automatic publication.

## KONTUR bridge

`integrations/kontur/ARTIFACT_ACQUISITION_INTAKE.v052.json` records current reviewed KONTUR LM1 and LM3-A artifact identities as `PREPARED_NOT_AUTHORIZED`. It does not create a transfer authority.

The generic request contract is under `integrations/artifact-acquisition/`.

## Delivery boundary

The exact local update is bound only to:

```text
workbench-v0.51.13-accepted
46c926221cfaa8be3b68012852c7e8f3e324247f
```

Public `main` remains deferred.
