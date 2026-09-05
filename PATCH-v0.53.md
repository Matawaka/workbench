# Workbench v0.53 — Bounded Runtime Execution Lease

v0.53 adds a provider-neutral, one-shot runtime execution primitive above separately materialized runtime-tree evidence.

## Boundary

```text
verified artifact bytes
        !=
materialized runtime tree
        !=
execution authority
        !=
process started
        !=
runtime ready
        !=
benchmark/model/game authority
```

The v0.53 execution layer does **not** extract archives or create a runtime tree. It accepts only a separately produced `matawaka.runtime-tree-manifest/v0.53` in state `MATERIALIZED_VERIFIED` and requires the runtime root to be outside the Workbench Git repository.

## Authority corridor

```text
exact execution JSON
        ↓
no-effect Preview
        ↓
explicit human confirmation
        ↓
one-shot Execution Lease
        ↓
EXECUTION_PREPARED
        ↓
lease call consumed durably
        ↓
manifest + executable size/SHA-256 revalidated
        ↓
Process.Start (UseShellExecute=false, exact ArgumentList)
        ↓
Windows process image path observed
        ↓
process image SHA-256 verified
        ↓
PROCESS_STARTED_VERIFIED
        ↓ optional bounded alive-after-delay observation
RUNTIME_READY_OBSERVED
```

## Fixed controls

- exact runtime-tree manifest path + SHA-256;
- exact executable relative path + SHA-256 + byte length binding;
- executable rehash immediately before `Process.Start`;
- observed Windows process image path/hash must match after start;
- `.exe` only; `cmd.exe`, PowerShell/pwsh, script hosts and loader/interpreter images are refused;
- `UseShellExecute=false`; arguments use `ProcessStartInfo.ArgumentList`;
- no elevation request;
- minimal inherited OS environment (`SystemRoot`, `WINDIR`, `TEMP`, `TMP`) plus bounded explicitly reviewed non-secret environment entries;
- one active bounded runtime per Workbench process;
- stop accepts no PID and targets only the exact in-memory Process object/tree created by the lease;
- failure/expiry/cancellation leaves no retry/resume/start authority.

## Non-effects

`Runtime Ready != Benchmark Authority` and `Runtime Ready != Model Request Authority` remain explicit. v0.53 does not grant KONTUR-specific behavior, game access, general process authority, arbitrary process kill, archive extraction, runtime materialization, benchmark or model requests.

The exact local predecessor for the v0.53 update is:

```text
workbench-v0.52.1-accepted
c1ec1c744e4f5fa0e5a6056d0230d4cb98e70b7f
```
