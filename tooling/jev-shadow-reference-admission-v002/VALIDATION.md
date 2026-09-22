# Validation record — 2026-09-22

Base branch: `handoff/jev-shadow-codex-2026-09-22`.
Exact base: `1ca1fdcc68d155a14b5e86c091342501e0297f5f`.
Implementation predecessor: `a6e389f1b2910e987f5749186152fb5d31f98eda`.
Successor branch: `codex/jev-corpus-reference-admission-v0.2`.
The exact final head and diff are recorded in the new Draft PR metadata/body (a file cannot contain its own commit SHA).

Initial checkout was clean on the implementation predecessor. The handoff commit existed locally and its ancestry from the predecessor was verified before creating the new branch. GitHub confirmed #118 is open/Draft at the exact handoff SHA; #115–117 remain open/Draft at their documented heads. No AGENTS.md was found in the working area or its applicable ancestors. `main` was not used as a substitute base.

## Actual local commands/results

Windows, Node `v24.19.0`, .NET SDK `10.0.401`. All commands ran from `K:\Matawaka\workbench` unless specified.

| Command | Actual result |
| --- | --- |
| `npm.cmd --prefix tooling/jev-system-one-judgment-adapter test` | 20 passed, 0 failed |
| `npm.cmd --prefix tooling/jev-shadow-one-shot-send-v003 test` | 4 passed, 0 failed |
| `npm.cmd --prefix tooling/jev-shadow-evidence-corpus-v001 test` | 7 passed, 0 failed |
| `npm.cmd --prefix tooling/jev-shadow-label-semantics-v002 test` | 5 passed, 0 failed |
| `npm.cmd --prefix tooling/jev-shadow-reference-admission-v002 test` | 76 passed, 0 failed, 0 skipped |
| `dotnet build tooling/jev-shadow-real-case-lab-v001/Capture.csproj -c Release` | Success, 0 warnings, 0 errors; executable not run |

Baseline total: **36/36**. Successor: **76/76**, including two tests reproducing the legacy defects before successor implementation. Total across the five suites: **112/112**.

New checks cover file hashes/BOM, false packet self-digest, candidate/request/receipt/corpus/labels/provenance/signoff/event bindings, unknown schemas/fields/signals, invalid numeric values, missing evidence/exposure, assisted-origin policy separation, reviewer separation, templates/migrations, N/A/undetermined, repeated and aliased sources, five cases times six signals, per-model/rubric/population cohorts, equal-group weighting/conflicting truth, Windows junction escape, public repository destinations, overwrite refusal, and actual CLI executions with synthetic input files. Human-only tests reject receipt input, nested model probability fields and exposed-label declarations, and verify the adjudication draft remains unfilled.

Before committing, `git diff 1ca1fdcc68d155a14b5e86c091342501e0297f5f --exit-code` confirmed every pre-existing tracked file was unchanged; only the new package and new workflow were untracked. The source scan found no provider/network/send imports or key references in the successor. The workflow checks the same narrow change surface and runs five offline suites on Linux/Windows with Node 22. Local execution does not claim remote CI success; the PR checks report actual remote results.

## Private case gate

A local audit inspected only the named case directory. The secondary's raw digest matched the previously submitted frozen original. All 13 existing files had identical before/after raw SHA-256 inventories. New manifests/reports were written append-only outside the repository; private contents, source hashes and per-signal judgments are not published here.

`preflight` returned **PRIVATE_INPUTS_UNAVAILABLE**, reference admission **HOLD**. Separate original request, receipt and corpus case record were not available in that directory. The archive's MISSING-ORIGINALS.json, addendum/provenance evidence, completion/identity/exposure evidence, policy/source grouping and completed adjudication also require operator evidence resolution. No absent original was reconstructed.

SECONDARY origin remains HUMAN_AI_ASSISTED by operator declaration. No completed HUMAN_ADJUDICATED reference exists. The next gate is an operator-reviewed complete original/evidence chain and a separately completed human adjudication with explicit exposure and reviewer provenance, under a predeclared policy. The coding agent's post-model analysis cannot supply that reference.

## Known limits and non-effects

See README for the exact contracts and limits: declared signoffs are not authenticated signatures; source grouping and policy chronology require human assessment; arbitrary source prose may leak model information; canonicalization and hostile concurrent filesystem changes are not fully addressed. Optional original provider-response absence remains explicitly declared, never represented as a verified response digest. All metrics remain diagnostic and operationalSpecificity stays RESEARCH_ONLY.

No provider invocation, `send:once`, `qualify:*`, capture, key read, lease activation, production threshold, production source edit, authority/readback change, historical evidence rewrite, merge, rebase, old PR retarget/closure or automatic human adjudication occurred.
