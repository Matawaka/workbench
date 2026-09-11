# Selected Qwen profile — code-only adapter and one-shot rehearsal

This additive successor starts at Workbench main
`58b9430fc544998a8e40ba00b6757cc630ba9081`. It leaves the old v0.55/source-bound
services, their public APIs, fixtures, permits, validators and refusal unchanged.
No UI/IPC route or executable transport is registered. This is NOT live support.

## Implemented boundary

`QwenIsolatedOneShotAdapter.Review` consumes the existing source/request record
types plus an exact isolation-requirements record. Closed JSON parsers reject
missing/extra/duplicate keys, ambiguous numeric representations and wrong types.
Reordered object fields are allowed; the rebuilt .NET serializer digest binds
ALL source/request/requirements fields and the foreground budget. It is a new
versioned serialization domain, not a replacement for a Python or old digest.

The selected `LLAMA_CPP_B10621_QWEN3_8B_Q4_K_M_V1`, exact selected model digest,
one process/request, 99 requested GPU layers, no CPU fallback, no network/server/
tools/game/display, 160 tokens/600 UTF-16 chars, 64 KiB request/stdout/stderr and
60-second source ceilings are retained. Smaller source limits are admissible.
Effective time is the minimum of source timeout, source TTL and caller-supplied
remaining foreground time (at most the existing 30 seconds). It cannot extend
the UI envelope. A supplied remaining time is not trusted currentness evidence.
The digest binds opaque file locators; it does NOT validate their filesystem
containment, existence, contents, manifest authenticity or model compatibility.

`RequireLiveAdmission` ALWAYS fails with
`HOST_ISOLATION_PROVIDER_NOT_QUALIFIED`. There is no production Grant/Invoke API,
injected approval boolean, accepted external isolation receipt or provider that
can bypass this guard. The plan has no authority and cannot drive the old
process-only service. All claims on it remain false.

The internal `QwenOneShotRehearsal` implements a **pure synthetic event machine**:
instance-owned ticket -> consumed attempt -> stream limits -> terminal candidate
or refusal. Cancellation, expiry, rollback, drift and stream overflow consume
the synthetic attempt irreversibly; a different instance cannot resume its old
ticket. A lock serializes concurrent attempts. Stream bounds are checked before
copying; stderr is counted, not retained. Strict UTF-8 decoding happens after
bounded chunk collection. Output binds review/envelope and exact text digest;
it remains explicitly synthetic/untrusted with no display or review authority.
Disposal clears buffered bytes; .NET immutable strings have no secure-erasure claim.

Synthetic time is in-memory logical milliseconds, not OS monotonic/reboot proof.
There is no SQLite/durable/global deduplication, real process cleanup, sandbox,
GPU enforcement, actual tokenizer verification or production identity here.
Network denial, GPU placement and process termination are **simulated input
conditions** in the rehearsal, never accepted as production proof. The model
text/token semantics, command arguments and real bytes remain unqualified.

## Why the live guard remains closed

The [selected llama.cpp source](https://github.com/ggml-org/llama.cpp/blob/c1d0e7a004015f23bc0233470b747b596f29b264/tools/cli/README.md)
documents GPU/fit and single-turn controls, but flags are not isolation evidence.
[Microsoft's AppContainer documentation](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
describes a native capability/token/resource boundary. This package does not
create a profile, grant filesystem access, change firewall/Windows policy or
prove CUDA compatibility inside that boundary. Loopback, a Job Object or an
absent network call in a wrapper cannot be relabeled as process-network denial.

A later separately qualified native host must bind actual image/model bytes,
restricted capabilities/handles/environment, process ownership, no-network and
GPU placement evidence before any model execution becomes admissible. A trusted
current model-specific decision and real request lease remain separate. KONTUR
must then perform response review and exact-display permission. This task does
not implement or authorize that native host or a live test.

## Qualification (no packages, application launch or model)

```text
python -I -B integrations/model-invocation/qwen-code-only/qualify.py --dotnet <existing-SDK-10.0.400-dotnet> --output <new-absolute-directory-named-qwen-code-only-qualification-*>
```

The qualifier builds only a net10.0 console probe with two byte-source-bound
historical **data record declarations**, the two new files and the synthetic
probe. It does not build the WPF application or invoke its existing PowerShell
Exec target. NuGet package sources are cleared in a new output-local config;
no packages are referenced. The existing SDK is required, never installed.
Run in the repository and byte-identical clean-unpacked bounded source; reports,
archive/source/DLL hashes and logs remain in that create-only output directory.
The archive is the bounded test-source closure, NOT a Workbench install candidate.
LF/CRLF materialization is explicitly normalized only for checking the three
historical Git blob identities; exact actual file hashes/archive bytes are
recorded separately, not declared identical across platforms.

No new workflow exists and no Linux/Windows native-host qualification is claimed.
UU-AAP remains semantics/provenance; Workbench technical execution authority;
KONTUR intent, companion policy, response review and exact display.
No model/runtime download/install/start, real lease, game access, credentials,
raw observation, network capability, output display, Stable Core/IP change or merge.
