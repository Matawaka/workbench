# Windows isolation probe — qualification tooling, not a model host

This additive successor investigates a missing native boundary beneath the
code-only Qwen adapter. It is **not registered** with Workbench's model service,
cannot issue a lease, and does not change KONTUR intent/review/display semantics.
The existing live-admission refusal remains unconditional.

Predecessor: `34e2af4e59ad5ba0ca209aa6334f0c7debb2bb24` (local code-only adapter).
Workbench main observed before this work:
`58b9430fc544998a8e40ba00b6757cc630ba9081`.
KONTUR main observed before this work:
`d155ba0a05325ba0fb57b7095ad91a67f4aaf456`.
None of these identities is substituted for another.

## Architectural split

- UU-AAP: semantics, invariants and provenance.
- Workbench: generic technical execution authority and its qualification.
- KONTUR: current user intent, companion policy, response review and exact display.

Native test evidence != production isolation provider.
Synthetic test receipt != authority. No Qwen compatibility or activation follows.
The selected model, CUDA profile, TTL and use budgets are unchanged.

## What the probe actually does

The explicit native test creates a fresh temporary AppContainer with zero
capabilities, pins four small test files, limits inheritance to three pipe handles,
creates one suspended test process, assigns an owned Job, queries its limits and
token, then resumes it only after verification. Job limits: one active process,
512 MiB process memory, kill on close and die on unhandled exception. Parent wait
is bounded to 10 seconds; failure cleanup uses only owned handles.

The child reads/writes only three synthetic sentinels, tries to start the same
fixed canary executable and attempts one connection to the parent's ephemeral
IPv4 loopback control listener. No Internet destination, DNS, game path, model
input, arbitrary command, production credential or user data is involved.

ACL changes affect only the newly prepared test directory and its deny sentinel.
Preparation and execution must use the same owner; foreign ownership is refused
before profile creation. No ownership takeover, existing desktop ACL, Windows
policy, firewall rule, loopback exemption or installed runtime ACL is changed.
Profiles are removed only after the owned child has exited. Test files and
historical results are retained. The profile-removal operation removes only the
profile created by that particular test invocation.

The fixed `DETACHED_PROCESS` variant allocates no console at startup. It does not
remove AppContainer, child-process restrictions or Job limits. It is not an
automatic less-isolated retry path. Parent environment secrets are not inherited:
the child receives an explicit allowlist, with its user/temp paths pointing to
the newly created AppContainer's own OS-created directory.

## Honest network semantics

The parent first proves its own listener accepts a normal local connection.
The child's explicit `WSAEACCES` (10013) is required by this probe's complete-PASS
contract; a timeout, connection refusal, unrelated exception or zero capabilities
alone does not satisfy that behavioral assertion. A missing connection is not
silently converted into proof of universal network isolation.

Actual testing reached a **timeout**, not an explicit access-denied socket error.
The overall result therefore remains **FAIL_CLOSED / NOT MODEL READY**, although
file controls, blocked child creation, token and Job observations succeeded.
See `STATUS.md` and generated evidence for exact identities and distinctions.

The child-creation denial allowlist is Win32 5 (`ERROR_ACCESS_DENIED`) or 367
(`ERROR_CHILD_PROCESS_BLOCKED`). 1282 is a stack-buffer-overrun error and is
explicitly rejected. A short-lived intermediate build with that erroneous
constant was caught during documentation review and was **never run natively**.

## Reproduce without native effects

Use an existing Windows x64 .NET 10 SDK and Python. No package feeds or runtime
installation are enabled. Supply a new output directory whose name begins with
`windows-isolation-reproduction-`:

```powershell
& $Python -I -B .\qualify.py --dotnet $DotnetSdk --output $NewOutput
```

This runs only `--unit`: repository source, byte-checked clean ZIP unpack and
three intentionally weakened copies. Source and candidate hashes are recorded
separately; identical DLL bytes across build paths are not claimed. The source
ZIP is a test-source artifact, not a deployable candidate. No workflow is added.

`verify_evidence.py --self-test` performs 26 read-only consistency/hostile checks
against the published JSON and current source hashes. It refuses history-to-PASS
substitution, fake cleanup, wrong candidate identity, boolean/integer confusion
and timeout-to-denial promotion. This checks consistency, not authenticity of an
independent observer. `full_build.py` separately compiles the exact predecessor
and runs its existing synthetic source-bound test; it does not run the GUI.

`prepare.py` alone also builds and runs pure tests only. It never starts the
native trial. `--native-trial` is a separate, one-attempt mode, guarded by the
specific historical two-hour window 2026-09-11 20:03:09–22:03:09 UTC and an atomic
create-only attempt marker. After that window it refuses. Do not rewrite that
window into a new authorization. Its wall-clock check is an operational guard,
not a durable anti-rollback authority-clock implementation.

## Limits and next safe work

This package does not prove GPU/CUDA access, pinned llama.cpp compatibility,
universal filesystem isolation, all network protocols, production token budgets,
full Workbench application conformance, Linux support, independent security
review or a real model-request/output/display lease. It cannot observe a game or
display model output. Loopback test traffic is a local test effect, not an
external network capability grant.

Next: review and qualify a precise network-evidence contract and the still
unimplemented production host. Do not weaken the existing live-admission gate
or count this partial probe as permission to run Qwen.

## Primary references

- [AppContainer implementation](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
- [Process creation flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags)
- [GetTokenInformation](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation)
- [TOKEN_ELEVATION](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_elevation)
- [Windows error definitions used by Rust's Windows implementation](https://github.com/rust-lang/rust/blob/main/library/std/src/sys/pal/windows/c/windows_sys.rs)
- [Win32 errors 1000–1299](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1000-1299-)
