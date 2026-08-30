# Security / authority boundary — v0.1.3

The workbench deliberately keeps authority narrower than capability.

1. JSON input is **data**, not a terminal command language.
2. Unknown `kind` values are rejected.
3. AgentHost cannot execute arbitrary processes.
4. Every `agent.run` produces a typed `CapabilityRequest` before provider execution.
5. A Workbench-local read-only capability policy produces an explicit `CapabilityDecision` with allow/deny, granted authority, mutation budget, network/process grants, reasons, and non-effects.
6. `observe` and `propose` can be granted only with `read-only`, mutation budget `0`, network `false`, and arbitrary-process execution `false`.
7. `execute` remains explicitly denied; a UI checkbox cannot promote it to mutation authority.
8. A denied capability decision prevents the repository provider from running.
9. Read-only inspection uses managed file APIs against repositories already present under the selected catalog root.
10. Evidence collection is bounded by at most 32 focus repositories, 64 terms, file count, per-repository candidate count, final evidence count, file size, text extensions, and ignored build/VCS directories.
11. The final evidence frontier uses deterministic round-robin selection across focus repositories so repository ordering cannot silently consume the whole global evidence budget.
12. Catalog inspection invokes only fixed read-only git commands (`rev-parse --abbrev-ref HEAD`, `rev-parse HEAD`).
13. Catalog refresh invokes only fixed `git fetch --all --prune`, remains off by default, and requires separate explicit UI permission.
14. Agent receipts state capability request, capability decision, evidence coverage, authority used, and mutation list.
15. Cancellation propagates through the command router.
16. Future semantic/LLM providers must consume the same typed authority decision and must not inherit ambient repository/process authority.
17. Future Execute must be a separate capability with an explicit bounded mutation budget and evidence-bearing outcome receipt.

Required invariants for this increment:

`Agent enabled != Execute authorized`

`Evidence budget != First repository monopoly`

`Agent completed != Repository changed`

`Capability available != Capability authorized`

Availability of a capability does not itself authorize execution.
