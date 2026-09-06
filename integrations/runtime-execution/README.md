# Generic bounded runtime execution — v0.53

This directory defines the provider-neutral evidence/request boundary consumed by Workbench v0.53.

## Inputs

1. `runtime-tree-manifest.v053.schema.json` — evidence from a **separate materialization layer**. The execution primitive accepts only `State=MATERIALIZED_VERIFIED` and does not extract or install anything itself.
2. `runtime-execution-request.v053.schema.json` — exact executable/arguments/working-directory/environment/TTL/readiness request reviewed before a one-shot execution lease may be granted.

## Invariants

- Verified artifact bytes are not a materialized runtime.
- A materialized runtime is not execution authority.
- Execution authority is not general process authority.
- Process start is not runtime readiness.
- Runtime readiness is not benchmark/model-request/game authority.
- Stop authority is not arbitrary process-kill authority.

The selected Workbench Local app is only navigation context. The primitive contains no KONTUR-specific behavior.

## Runtime-root separation

Both the runtime-tree manifest and `RuntimeRoot` must resolve outside the Workbench Git repository. Reparse-point paths are refused. The exact executable must be listed by the runtime-tree manifest with byte length and SHA-256 matching the execution request.

## Start boundary

After explicit human confirmation, Workbench creates a one-shot lease. The call budget is persisted as consumed **before** the process is started. Immediately before `Process.Start`, the manifest digest and executable bytes/SHA-256 are revalidated. The process uses `UseShellExecute=false` and `ArgumentList`; shell/interpreter images are refused. After start, Windows is queried for the actual process image path, and that file is hashed again.

## Stop boundary

The v0.53 stop action accepts no PID. It can target only the in-memory `Process` object created by the exact execution lease and revalidates image path/start-time ownership before requesting `Kill(entireProcessTree: true)`.

This is an authority/evidence primitive, not an OS sandbox. It does not claim to remove capabilities inherent in the executed binary; callers must make separate decisions about network, model, benchmark and application authority.

## Provenance-bound outer lease — v0.55

`runtime-execution-source-binding.v055.schema.json` supplies the missing generic
source-intent binding above v0.53. A source record must identify an exact repository
frontier, source artifact digest and request-envelope digest while explicitly stating
that the record itself grants no authority.

`ProvenanceBoundRuntimeExecutionV055Service` re-runs the v0.53 preview when authority
is requested and binds that fresh request digest to the source evidence. Only a
separate explicit confirmation can create the one-call outer lease. The inner v0.53
bearer is retained only in the creating service instance; it is not returned to the
caller or written by v0.55. A restart therefore cannot resume the authority.

The outer lease is consumed durably before delegating to the separately consumed
v0.53 lease. Its receipt binds the source and both authority layers. It does not add
model-request, network, game or display authority, and it remains provider-neutral.

For KONTUR this closes only the technical process-lease provenance edge:

```text
KONTUR handoff evidence (authority = none)
        -> Workbench v0.55 source/request binding preview (effect = none)
        -> separate explicit confirmation
        -> v0.55 outer one-shot lease (inner bearer hidden)
        -> unchanged v0.53 exact runtime execution lease
```

It does not implement the later local-model request/output transaction.
