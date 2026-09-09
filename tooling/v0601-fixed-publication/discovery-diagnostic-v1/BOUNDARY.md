# v0.60.1 receive-discovery diagnostic — NOT a publisher

## Incident and invariant
The original attempt SHA-256 is `5c9334fe696f6eee437b2abbee299ece81985032310fc64301abdca5d03a37e1` (1306 bytes); original outcome is `d5cd6b21b319803272c25983054ac1fad7e27cad8a654e69c8dfcca01f05d10e` (1282 bytes). The original preflight remains `c7a7dae81f93c783629f3fc5686aec5a2fb45533d95a5413628effa5763e1671` (6187 bytes).

The v1 publication attempt remains consumed and UNVERIFIED_NO_RETRY. A fresh observation cannot reconstruct the discarded historical stderr, prove that no historical server effect occurred, or rearm that attempt. Do not rerun/update/rename the publisher or delete its files. Do not merge tooling to bound main.

## Closed operation
A separate zero-argument EXE uses the existing exact 365-file MinGit distribution. It validates the original eleven records, current V2 snapshot and prior transport evidence locally before preview. It compiles the original V2 verifier and pure file/lock/tool/credential support, but **does not compile Publisher.cs, its transport, guard or publication entry point**.

After new explicit `DIAGNOSE-EXACT-V0601-RECEIVE-DISCOVERY-NO-PUSH`, hidden local PAT input and fresh revalidation, it writes a new diagnostic consumed-attempt file before any network. It creates a NEW empty bare context with no copied objects, refs, source config, hooks or credentials. It then invokes the exact native `git-remote-https.exe` with the fixed GitHub endpoint twice as arguments, and ONLY this stdin:

```
capabilities
list for-push

```

No `push`, `fetch`, `import`, `export`, `connect`, `stateless-connect`, refspec or pack body is sent. This is the reference-discovery phase of the receive service, not an update request. One helper invocation is the budget; this is not an unqualified claim that native HTTP never performs multiple transport-level operations. Qualification explicitly counts GET and POST requests for positive/negative loopback cases.

Credential is input locally, never sent to the assistant, stored in config/receipt/command arguments, or read from stored credential helpers. It is used in the child runtime environment scoped to the exact URL. Environment/tracing/proxies/cookies/config inheritance is excluded. TLS validation remains enabled with exact MinGit CA bytes. Redirects and automatic HTTP retries are disabled. A temporary credential should be revoked after the probe; do not broaden scopes in response to an UNKNOWN result.

## Observation and limits
Both streams are drained concurrently, capped at 512 KiB each. Stream overflow signals immediate cancellation. The main probe deadline is 30 seconds with bounded cleanup waits; cleanup uncertainty is recorded. Process start/OS scheduling and privileged-process isolation are not proven. This fixes diagnostic-runner observation rather than changing the historical publisher.

The report contains only safe allowlisted error categories, exit code, captured lengths/EOF/overflow flags, cleanup status and selected main/current/historical refs. Raw output and secret-derived digests are NOT persisted. Ambiguous or unrecognized errors remain explicit. A negative credential response describes this new observation, not the old attempt's cause. Ref discovery does not qualify atomic push, hook execution, repository rules or actual write permission.

The preserved original publisher regression suites separately cover atomic/guard/lost-response behavior on disposable fixtures. No new production push is admitted by these tests.

## Paths and stop
Reuse existing MinGit at `K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit`; preserve original transport at `K:\Matawaka\Tools\WorkbenchV0601FixedPublicationV1`.
The diagnostic itself creates `K:\Matawaka\Tools\WorkbenchV0601ReceiveDiscoveryV1` — do not precreate/reuse it.
New files under `K:\Matawaka\Workbench\artifacts\publication-v0601`:
- `attempt-discovery-diagnostic-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json`
- `discovery-diagnostic-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json`

A recorded diagnostic can contain a refused connection. It is never publication PASS. Return both new original JSON; on refusal preserve any created files and report the safe error, without repeating the probe. No Update/Accept/import/standalone-preflight replay, source mutation, main/tag change, token disclosure or retry authority.

## Primary protocol references
- https://git-scm.com/docs/gitremote-helpers — capabilities, list for-push and separate push command.
- https://git-scm.com/docs/gitprotocol-http — info/refs discovery vs receive-pack POST update request.

Status at creation: implementation only; operator delivery additionally requires exact-source Windows qualification and independent downloaded-artifact audit.
