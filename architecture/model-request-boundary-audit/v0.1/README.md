# Workbench model-request boundary audit v0.1

Status: **read-only architecture decision / no model or runtime authority**.

Origin accepted/public Workbench frontier:

- `65b0b49a513a6b782760a7626d6b768bf7bb7f91`
- `workbench-v0.54.2-accepted`

The accepted real-host corridor now proves three deliberately separate generic layers:

```text
v0.52  ACQUISITION_VERIFIED
  -> v0.54  MATERIALIZED_VERIFIED
  -> v0.53  RUNTIME_READY_OBSERVED
```

This audit asks what must come next **without** reinterpreting generic process execution as model-request authority.

## Critical finding

`BoundedRuntimeExecutionV053Service` is an exact-process launcher, not a model-request protocol. It:

- binds a `MATERIALIZED_VERIFIED` runtime-tree manifest;
- verifies the exact executable and re-hashes it before `Process.Start`;
- rejects shells/interpreters and uses `ProcessStartInfo.ArgumentList`;
- permits a structurally bounded caller-supplied argument vector;
- records the explicit invariant `Runtime Ready != Model Request Authority`.

It does **not** bind/reverify a model artifact as request evidence, count one model request, capture a bounded model result from stdout, or issue a portable model-output receipt. Therefore an exact process invocation cannot be silently relabelled as proof of a separately governed model request.

The accepted v0.53 real-host smoke remains valid: it ran a non-model test image. The finding is about future semantic reuse, not a retroactive invalidation.

## Selected first implementation profile

The audit selects:

```text
V055_BOUNDED_LOCAL_MODEL_INVOCATION_LEASE
profile = DIRECT_SUBPROCESS_STDIO_ONE_SHOT
```

as the first implementation candidate.

Why this is first:

- one process and one request can be made explicit in one new authority class;
- no server, port or network authority is needed;
- exact runtime and exact model can be independently reverified;
- stdin/stdout/stderr can receive independent byte ceilings;
- timeout/output-overrun termination can remain exact-owned-process-only;
- the resulting output can remain untrusted and stop before response/display policy.

This is **not** authority to implement or run it. A separate successor must materialize v0.55.

## Deferred profile

A long-lived loopback model session plus a separate request lease remains architecturally useful, but it requires new evidence for:

- loopback endpoint identity;
- server process ownership;
- model-loaded/session identity;
- request-to-session binding;
- explicit local-network/port semantics.

It is therefore `DEFER_SEPARATE_SUCCESSOR`, not a silent extension of v0.53.

## Why direct v0.53 reuse is rejected

`REUSE_V053_AS_MODEL_REQUEST` is `REJECT_CURRENT_EVIDENCE`.

The issue is not that v0.53 is unsafe for its admitted purpose. The issue is that its admitted purpose is exact process execution. Its argument vector is structurally bounded but domain semantics are not classified, and its execution receipt's model-request field does not create a model-request protocol.

```text
Exact Argument Vector != Classified Model Request
Process Execution Authority != Model Request Authority
Model Request Authority != Response Authority
```

## Required v0.55 evidence chain

A future v0.55 implementation must require both:

1. exact Workbench-owned v0.54 `MATERIALIZED_VERIFIED` runtime-tree evidence; and
2. exact Workbench-owned v0.52 `ACQUISITION_VERIFIED` model-artifact evidence.

It must re-hash both the runtime executable and model artifact immediately before the request-bearing process is created.

The caller must not be able to turn arbitrary command-line strings into a model request. The future provider adapter/profile must generate the exact allowed model-command shape from typed request fields.

## Output boundary

Durable evidence should bind request digest/size and output digest/size. Raw prompt persistence is not required by default.

A successful result may produce only an **untrusted local model output candidate**. It does not create content review, factual truth, response authority, display permission, game authority, benchmark authority, ActionPermit or successor authority.

## Remote-smoke hygiene dependency

Before a future real-host remote smoke, Workbench #73 must be obeyed mechanically: `ExpectedBytes` and `ExpectedSha256` are derived only from immutable served bytes re-fetched after upload.

## Validation

```powershell
python architecture/model-request-boundary-audit/v0.1/validate.py
python architecture/model-request-boundary-audit/v0.1/test_audit.py
```

The audit is source-byte bound and fail-closed. It modifies no acquisition/materialization/execution primitive and performs no runtime effect.
