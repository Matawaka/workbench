# Matawaka Workbench

Independent Windows workbench for evidence-bounded development and analysis over a local Matawaka repository catalog.

## v0.6 checkpoint

v0.6 preserves the accepted Workbench chain through v0.5 and adds a real Windows restricted-token launch for the fixed semantic host.

The semantic path is now:

```text
JSON command
  -> typed read-only authority gate
  -> balanced evidence frontier
  -> fixed SemanticHost SHA-256 verification
  -> restricted primary token (DISABLE_MAX_PRIVILEGE)
  -> Low integrity token (S-1-16-4096)
  -> CreateProcessAsUser(CREATE_SUSPENDED)
  -> Job Object assignment
  -> ResumeThread
  -> sanitized stdin packet
  -> typed stdout receipt
  -> parent input/output digest verification
  -> proposal
```

The semantic provider still receives only bounded evidence/provenance data and authority receipt data. Repository roots, file handles, executable paths, network clients/credentials and mutation authority are not included in semantic IPC.

## Security-context facts

v0.6 receipts are intended to show:

- `RestrictedToken = true`;
- `MaximumPrivilegesDisabled = true`;
- `LowIntegrityLevel = true`;
- `IntegrityLevelSid = S-1-16-4096`;
- `CreatedSuspended = true`;
- `JobAssignmentBeforeResume = true`;
- `JobObjectApplied = true`;
- `ActiveProcessLimit = 1`;
- `ProcessMemoryLimitBytes = 268435456`;
- `BreakawayAllowed = false`;
- `NetworkIsolationEnforced = false`;
- `OsSandbox = false`;
- `SameUserIdentity = true`;
- `SameUserSecurityContext = false`.

The distinction is deliberate. A restricted low-integrity child is a meaningful reduction of Windows privileges/write-up ability, but it is not AppContainer, network isolation, filesystem namespace isolation, or VM containment.

## Authority

`freeshield-read-only-bridge/v0.6` grants only read-only Observe/Propose when explicitly enabled and when requested mutation/network/arbitrary-process authority remains zero/false.

`Execute` remains denied before evidence collection and before semantic process launch.

## Provider registry

Built-in offline provider IDs remain:

- `local-contract-synthesis-v0.3`;
- `deterministic-evidence-semantic-v0.2`.

Provider identity remains data inside a closed registry. No provider assembly/executable/path may be selected from command JSON.

## Provenance and line endings

`.gitattributes` keeps textual repository content normalized to the declared line-ending policy. Workbench maintains accepted local Git predecessors/tags before each larger boundary change.

Catalog repositories remain evidence sources and are not modified by the v0.6 patch.
