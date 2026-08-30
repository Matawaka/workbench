# Matawaka Workbench

Independent Windows workbench for bounded analysis and development flows over the local Matawaka catalog.

## v0.3 checkpoint

```text
JSON command
  -> terminal-safe router
  -> catalog snapshot (repo + branch + HEAD)
  -> typed read-only capability decision
  -> balanced evidence frontier
  -> offline semantic-provider selection
  -> sanitized evidence packet
  -> interchangeable provider
  -> typed semantic analysis/boundary receipts
  -> proposal/checkpoint
  -> STOP before materialization or mutation
```

The Windows UI exposes separate tabs for Events, Result, Evidence Receipt, Authority Receipt, Liveness, Semantic Provider and Agent.

### Provider substitution

`SemanticProviderRegistry` is Workbench-local and deliberately not promoted as a UU-AAP reusable/Stable Core abstraction. v0.3 ships two offline implementations:

- `local-contract-synthesis-v0.3`;
- `deterministic-evidence-semantic-v0.2`.

Both receive the same sanitized `SemanticEvidencePacket`. The packet contains evidence snippets, repository identity/branch/HEAD, balanced coverage and typed authority receipt. It contains no repository roots, file handles, process execution, network access or mutation authority.

Provider selection and analysis are separately receipted. The semantic input/output digests make substitution inspectable. v0.3 uses built-in in-process providers only; this is an API/data-boundary proof, not an operating-system sandbox for hostile provider code.

### Exact source frontier

Before semantic analysis, v0.3 requires the locally observed `uu-aap` focus frontier to match:

`f5673a39ddeef05f82c828f6cff554518f5f8ef6`

The exact source bindings cover PCL progress/human-view, Scoped Authority Evidence, Materialization Authority and the Reusable Component Admission Audit. These are compatibility/reference bindings; canonical JavaScript/Python evaluators are not executed by Workbench.

### Admission guard

Workbench v0.3 follows the repository's `NO_ADMISSION` result conservatively: provider registry convenience is a Workbench-local composition, not evidence for Stable Core or interface-registry promotion.

### Authority

`freeshield-read-only-bridge/v0.3` grants only read-only Observe/Propose when explicitly enabled and when mutation/network/process requests remain zero/false. Execute is denied.

`Evidence != Authority`

`Provider Selection != Authority Grant`

`Scoped Authority Evidence != Materialization Authority != Execution Authority`

`Supported Evidence != ActionPermit`

No materialization or execution path exists in v0.3.

## Local layout

Workspace: `K:\Matawaka`

Catalog: `K:\Matawaka\Catalog`

The Workbench has its own local Git predecessor chain. Catalog repositories remain evidence sources and are not modified by the v0.3 patch.
