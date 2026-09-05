# Workbench v0.53.2 — Real-host Admission and Fixed Publication Closure

Exact local predecessor:
- `49ccefc68ec0b6979fd2e36c59af1e8f1f68de64`
- `workbench-v0.53-accepted`

Target local accepted tag:
- `workbench-v0.53.2-accepted`

`v0.53.1` is intentionally skipped as an accepted version. Issue #72 was a process-start diagnostics candidate and was closed `not planned` after the exact local real-host smoke proved the existing v0.53 execution primitive can reach `RUNTIME_READY_OBSERVED` and exact-owned stop. v0.53.2 does not silently reuse or reinterpret that candidate.

## Real-host gate already observed on accepted v0.53

The accepted local v0.53 frontier completed:

```
exact MATERIALIZED_VERIFIED runtime evidence
→ explicit one-shot execution lease
→ authority consumed before Process.Start
→ executable SHA-256 revalidation
→ exact Windows process image path/hash verification
→ RUNTIME_READY_OBSERVED
→ exact-owned-process stop
→ OWNED_PROCESS_TREE_STOPPED
```

Observed stop properties include:
- `ExactOwnedProcessVerifiedBeforeStop=true`
- `EntireOwnedProcessTreeStopRequested=true`
- `ProcessExited=true`
- `ArbitraryPidAccepted=false`
- no general process-kill authority

## Change

v0.53.2 adds only:
1. exact local checkpoint admission above accepted v0.53;
2. local validation of the Workbench-owned real-host execution + stop receipt pair;
3. explicit fixed GitHub publication authority after a no-network preview.

The v0.53 bounded runtime execution primitive is not changed.

## Fixed publication corridor

```
accepted local v0.53.2 HEAD + exact parent/tag
        ↓
real-host RUNTIME_READY_OBSERVED receipt
        ↓
matching OWNED_PROCESS_TREE_STOPPED receipt
        ↓
no-effect local publication Preview
        ↓
explicit human Publish accepted confirmation
        ↓
fixed github-workbench remote only
        ↓
remote main exact-base check
        ↓
fast-forward exact accepted HEAD
        ↓
publish current workbench-v0.53.2-accepted tag
        ↓
remote main/tag exact re-verification + receipt
```

Preview performs no `ls-remote`, remote-add, push, runtime start/stop, acquisition or materialization.

## Remote boundary

Fixed remote:
- `https://github.com/Matawaka/workbench.git`

Expected previously published `main`:
- `632ddbb73e8d70b485f02d21f772674d429adf8c`

Publication refuses a divergent remote main or conflicting current accepted tag. No force push or arbitrary ref/remote is available.

Intermediate local accepted tags remain local. Their commits may enter remote ancestry through the exact fast-forward, but ancestry does not silently promote those tags as separate remote accepted frontiers.

## Preserved non-effects

- no change to v0.53 execution primitive;
- no automatic publication;
- no automatic retry;
- no arbitrary Git command/remote/ref;
- no force push;
- no runtime start/stop during publication;
- no artifact acquisition or archive extraction/materialization;
- no benchmark, model request or game access;
- no catalog mutation or Agent Execute/ActionPermit;
- no private app/runtime bytes published by this layer.
