# Code-only checkpoint — 2026-09-11

**CODE_ONLY_QWEN_ADAPTER_AND_REHEARSAL_QUALIFIED_NOT_LIVE_READY**

Implemented locally in branch `feat/workbench-qwen-isolated-oneshot-code-only`,
derived from exact GitHub-observed main `58b9430fc544998a8e40ba00b6757cc630ba9081`.
GitHub source observation succeeded. Terminal `git fetch origin main` failed to
connect to github.com:443; no alternate credentials, policy change or escalation
was used. The already available exact commit is the base, not a guessed cached main.
No remote publication or PR was attempted by this code-only implementation.

## Exact application delta

- Added `src/Matawaka.Workbench.App/QwenIsolatedOneShotAdapter.cs`.
- Added `src/Matawaka.Workbench.App/QwenOneShotRehearsal.cs`.

Six package files provide the probe, qualifier, scope, documentation and report.
No existing application, fixture, validator, workflow, schema, Stable Core or IP
file changed. Main and installed applications are untouched. No runtime/model
was downloaded, installed or invoked; no game/UI/credential access or real permit.

## Qualification

Windows, existing .NET SDK 10.0.400, net10.0 isolated console probe: **101 PASS,
0 FAIL**, both from repository source and byte-verified clean-unpacked source.
Compiler warnings/errors: 0/0. Nine-file bounded test-source ZIP SHA-256:
`650bd93ea670f7cb0a4602ef418b4f61f16fd2c17cf88176a73dd422751b6d0c`.
This is not an install candidate. Actual source and DLL hashes are in
QUALIFICATION-SUMMARY.json. Repository/clean DLL identities are recorded
separately; binary equality across paths is not claimed.

Controlled negative mutations of NEW files in separate source copies produced:

| Removed guard | Expected RED actually observed |
| --- | --- |
| Live-admission refusal | 1 failing test |
| One-shot state check | 7 failing tests |
| Temporal boundary | 5 failing tests |

The actual source retains all guards and passed 101/101 in both contexts.
These are new-test sensitivity checks, not rewritten alpha.21/alpha.22 findings.
The preceding 100-test local run remains separate historical evidence; one new
invalid-surrogate locator check and unique case labels were then added, along
with mutation checks and disabled build-server reuse in the qualifier.

Unchanged KONTUR lifecycle 33, translator 20 and bridge 48 hostile controls were
also rerun successfully against their pinned source and current Workbench main.
That does not qualify a new live integration or change the frozen #52/#100 pair.
The original full Workbench C# probe/WPF app was NOT rebuilt here: this qualifier
deliberately compiles only historical data declarations and the new pure code,
without the old WPF PowerShell Exec target. No CI, Linux or native sandbox test
was performed. Do not turn this bounded test result into full application PASS.

## Remaining implementation boundary

Profile review and simulated lease behavior now exist. Actual Qwen process
isolation/execution does not. The public live-admission guard always refuses.
The internal rehearsal returns only `SYNTHETIC_UNTRUSTED_CANDIDATE_NOT_MODEL_OUTPUT`;
it cannot issue a real lease or authorize the old process service.

A separately scoped native-host implementation/qualification is still required
to prove network isolation, permitted file/handle/environment access, GPU-only
model placement, bounded termination, trustworthy token count and exact binary
identity. Code-only approval is not permission to create OS sandbox profiles,
change policies or run a real model. No fake bool/receipt is used as OS proof.

Independent security review, installed-user-host replication, current human
intent, real request/output lease, KONTUR review/exact display and live model
compatibility remain separate gates. The runtime profile and existing budgets
are not changed; no deployment/activation/merge authority follows.
