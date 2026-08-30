# Matawaka Workbench

Independent Windows workbench for bounded analysis and development flows over the local Matawaka catalog.

## v0.4 checkpoint

```text
JSON command
  -> terminal-safe router
  -> catalog snapshot (repo + branch + HEAD)
  -> typed read-only capability decision
  -> balanced evidence frontier
  -> offline provider selection
  -> sanitized evidence packet
  -> fixed separate semantic-host process
  -> verified semantic stdout receipt
  -> typed semantic/process boundary receipts
  -> proposal/checkpoint
  -> STOP before materialization or mutation
```

The Windows UI exposes Events, Result, Evidence Receipt, Authority Receipt, Liveness, Semantic Provider, Process Boundary and Agent tabs.

### Fixed semantic host process

v0.4 moves provider implementation out of the Workbench WPF/Runtime process. The parent starts only:

`Matawaka.Workbench.SemanticHost.exe`

from the fixed published `semantic-host` directory. Provider ids remain a closed built-in registry:

- `local-contract-synthesis-v0.3`;
- `deterministic-evidence-semantic-v0.2`.

The child receives one sanitized JSON packet over stdin and returns one JSON receipt over stdout. JSON cannot provide an executable path, DLL, script or dynamic assembly path.

The child environment is rebuilt from an explicit allowlist and uses an isolated temporary working directory. Parent-side code enforces timeout, cancellation, input/output size limits, provider identity, input digest and output digest verification.

### Honest isolation boundary

The child uses the same Windows user security context as Workbench. It is not an AppContainer, restricted token, ACL sandbox, VM or network sandbox. Therefore v0.4 claims a **separate-process + constrained IPC boundary**, not hostile-code containment.

`Process Isolation != OS Sandbox`

`Not Supplied Through IPC != Impossible For Same-User Hostile Code`

The shipped host and providers are fixed local code and perform no network model calls or repository mutation.

### Exact source frontier

Semantic analysis remains fail-closed on local `uu-aap` focus HEAD:

`f5673a39ddeef05f82c828f6cff554518f5f8ef6`

PCL, Scoped Authority Evidence, Materialization Authority and Reusable Component Admission Audit bindings remain exact-frontier compatibility/reference bindings. Workbench does not claim canonical evaluator execution.

### Authority

`freeshield-read-only-bridge/v0.4` grants only read-only Observe/Propose when explicitly enabled and when requested mutation/network/arbitrary-process authority remains zero/false. The trusted fixed semantic-host launch is an internal implementation step and is not exposed as arbitrary-process authority to JSON or providers.

Execute remains denied before evidence collection and before semantic process launch.

## Local layout

Workspace: `K:\Matawaka`

Catalog: `K:\Matawaka\Catalog`

The Workbench has a local accepted Git predecessor chain. Catalog repositories are evidence sources and are not modified by the v0.4 patch.
