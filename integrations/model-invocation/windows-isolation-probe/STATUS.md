# Native qualification checkpoint — 2026-09-11

**PARTIAL_NATIVE_BOUNDARY_OBSERVED_NETWORK_PROOF_INCOMPLETE**

This is a development/test successor to local commit
`34e2af4e59ad5ba0ca209aa6334f0c7debb2bb24`, not production host activation.
GitHub main was independently observed and re-observed unchanged at
`58b9430fc544998a8e40ba00b6757cc630ba9081`. No installed application or main changed.
No new application files are added by this package; all new files are under
`integrations/model-invocation/windows-isolation-probe/`. The predecessor added
two pure application files; their unconditional live-admission refusal remains.

## Final observations

| Check | Repository build | Clean-unpacked build |
| --- | --- | --- |
| Pure and hostile controls | 79 PASS | 79 PASS |
| AppContainer / zero capabilities / exact package / low integrity / not elevated | observed by parent | observed by parent |
| Owned Job and exact process/memory/kill limits | queried before resume | queried before resume |
| Allowed synthetic read | PASS | PASS |
| Denied synthetic read and write | PASS | PASS |
| Child-process creation | Win32 367, blocked | Win32 367, blocked |
| Normal parent's loopback control | connected | connected |
| Isolated child's TCP attempt | timeout, not WSAEACCES | timeout, not WSAEACCES |
| Synthetic files unchanged | true | true |
| Child exited / temporary profile removed | true / true | true / true |
| Overall native result | FAIL_CLOSED | FAIL_CLOSED |

Three deliberate source mutations (capability count, Job process count, receipt
boolean enforcement) each compiled and produced the expected RED unit outcome.
They were made only in separate generated copies, never the application source.
The old failed native results were not overwritten or reinterpreted as PASS.

The exported evidence passed 26 additional read-only consistency/hostile checks,
including exact source/candidate binding, closed envelopes, strict booleans,
derived counts and preservation of uncertainty. This is not observer identity
authentication or an independent security review.

Across the recorded campaign: **10 native attempts, 10 profiles created and
removed, 8 test processes created and observed exited**. Two attempts failed
before creating a process. No Qwen, llama.cpp, game, UI display, production lease,
credential, firewall rule or global policy mutation occurred. Local synthetic
test effects did occur; this is not described as a zero-effect audit.

Final four-file test source ZIP SHA-256:
`926dcfe8dcdbfe1c137f401916c1c024aca56bfd0420e969e33aef9e4a5a0282`.
Final repository probe DLL:
`317738eb9f0671c632d9a2f92a2a1e549978e871b175f0fe9d44c8843ae83a56`.
Final clean probe DLL:
`5b2084b56a0f94d9e3ba44b63d52b4b9787c3123a6dddf70bf097a8e7463758d`.
The different DLL hashes reflect separate build identities; byte-identical
binary reproducibility is not claimed. Source bytes match exactly.

## Development failures preserved

- Early restore requested an unavailable apphost patch; corrected to use the
  existing SDK pack. No runtime/package download was performed.
- A nullable compiler error was fixed before any native invocation of that copy.
- Preparation/execution under different accounts caused an ACL refusal. The
  foreign-owned directory was not taken over; a new same-owner copy was prepared.
- The initial minimal child environment returned Win32 203. The revised explicit
  environment allowed process creation; the exact missing variable is not proven.
- A zero-buffer TOKEN_ELEVATION size query returned Win32 24. The fixed DWORD
  structure query succeeded without weakening token checks.
- CREATE_NO_WINDOW variant exited `0xC0000142` before managed test execution.
  The later detached-console variant ran the probe with the same security
  attributes. Console startup is implicated, but exact failing DLL is unknown.
- Intermediate native diagnostics did not retain closed child failure fields;
  v0.2/v0.3 added them without copying raw exception text or modifying old receipts.
- A mistaken 1282 child-denial constant in a build-only intermediate copy was
  caught against documentation before native execution. It is now a hostile
  negative case, not accepted evidence.
- Final networking behavior is a timeout. No late-stage change converted that
  into a successful denial assertion.

## Full predecessor application compatibility

The exact committed code-only predecessor was also exported into a separate
clean full-source tree and the actual WPF Workbench dependency graph compiled:
**0 warnings, 0 errors**. The existing source-bound model probe then reported:
`SOURCE_BOUND_MODEL_PASS hostile=26 translation_roundtrip=true synthetic_grant=true live_model=false process_start=false`.

Full source ZIP SHA-256:
`01ceed55ea6324b57489e6bce0fe42ece7b926ae698bef9f6503489d2a90941a`.
This verifies compilation and the unchanged synthetic path, not a full GUI test,
installation, live-model launch or production authority transaction. The existing
icon-decoding build target ran only in the new build tree; installed binaries and
global PowerShell policy were not changed. Raw build logs remain local.

## Stop line and next bounded frontier

Do not register this test helper as a production isolation provider. Do not
remove `HOST_ISOLATION_PROVIDER_NOT_QUALIFIED`. The next implementation needs a
reviewed network-evidence contract and source/host-bound production attestation,
then exact CUDA/GPU/runtime resource qualification. None follows from token
shape, a merged PR, elapsed development time or a synthetic receipt.

No independent review, Linux qualification, production identity, real request,
output lease, response review, exact display permit or KONTUR activation is
claimed. The historical alpha.21/alpha.22 evidence and #52/#100 split are intact.
