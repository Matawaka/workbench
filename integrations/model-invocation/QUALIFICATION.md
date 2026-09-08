# Local qualification receipt — 2026-09-07

Base: `6541dc32182c970c8e1a6ade426a6cee7086511b`.
Environment: Windows, .NET SDK 10.0.400, Release build.
Evidence below is a local development observation, not a signed attestation or
a grant. No real runtime/model/UI/game execution was performed.

| Check | Observed result |
|---|---|
| Full Workbench solution build | PASS; 0 warnings, 0 errors |
| source-bound-model/Probe.csproj | PASS; 26 hostile refusals, shared fixture round-trip, synthetic grant, process_start=false |
| v055 process-provenance probe | PASS; 8 offline controls, 10 hostile refusals; no process start |
| v056 provenance-admission probe | PASS; 20/20 hostile checks; effects NONE |
| v057 provenance-capability-evidence | PASS; 20 hostile checks; no authority promotion |
| v058 live-capability-evidence-audit | PASS with recording provider; no external effect |
| v059 authority-evidence-review | PASS; pending human UI review, no UI launched |
| v060 live-authority-evidence-surface | PASS with synthetic command/provider results; no authority change |
| Historical v0561 provenance-review | FAIL: stale startup-source assertion |
| Historical v0562 acceptance | FAIL: stale startup-source assertion |

No aggregate all-green claim is made.

## Existing-main assertion drift (not repaired)

`.github/qualification/v0561-provenance-review/Program.cs:141` expects an exact
`if (!reviewOnly)` block calling both `ConfigureV0552Routing` and
`ConfigureV0552AcceptanceRouting`. That block does not exist in observed main.

`.github/qualification/v0562-acceptance/Program.cs:28` expects
`window.ConfigureV0561ProvenanceReviewRouting(reviewOnly);`. Current main instead
uses `provenanceReviewOnly`, under an additional capability-review fence.

The inspected `src/Matawaka.Workbench.App/App.xaml.cs` is unchanged by this
candidate (`git diff HEAD -- <path>` empty); `git show <base>:<path>` confirms the
same existing startup code. Normalizing CRLF does not restore the old v0561
assertion. The history includes startup changes in #89 / v0.56.2 and #96 / v0.59.
These are historical-test/current-main incompatibilities, not new test passes
and not permission to repair unrelated startup or historical acceptance.

Both failing console probes reported unhandled InvalidDataException and did not
exit promptly in this environment. Only the exact qualification processes were
stopped (the later probe had a 30-second execution ceiling). No product/game
process was stopped. A first v058 runner attempt used the wrong target directory;
rerunning its actual net10.0 project with --no-build passed. This is not classified
as a v058 product failure.

## Scope of new proof

New tests create only synthetic temporary model/runtime evidence and authority
state, then remove their own temporary workspace. The fixture executable bytes
are deliberately non-executable. The positive output test validates fabricated
typed records through the pure output binder. It does **not** prove actual
stdout generation, llama.cpp behavior, inference latency, GPU-only execution,
crash recovery of a real model process or OS-enforced network isolation.

Shared translator fixture SHA-256 as locally read in both candidates:
`7561ffe4ec1857cfcb6ea491745c95a5f67a9193ce21e801510ea5a60430c7c6`.
That is the exact file-byte hash in this checkout (including its line endings),
not an authority digest. The semantic round-trip also verifies deserialized
objects independently of file line endings.

The shared Git blob (line-ending-independent repository representation) is
`3409b9f71c0f47f286fd191994857b8e3c862825` in both candidates.

Source schema keys, one-call boundary and hard maximum ceilings are compared to
the C# record and existing service constants by the new probe. This does not
claim execution of a full JSON Schema engine.

No workflow changed and no new automatic CI coverage is claimed. Reproduce with
the commands in README. The two legacy failures remain visible for a separate
qualification-maintenance decision.
