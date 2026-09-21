# Workbench ↔ Jev observation-only shadow bridge v0.1

Status: **research-only / local export / no provider invocation / no authority readback**.

This integration is deliberately smaller than a live Jev provider bridge.

## Boundary

```text
CommandRouter / DevelopmentAgentHost
        |
        |  authority path completes
        v
CommandResult
        |
        +--> RenderResult
        |
        +--> ApplyTerminalState
        |
        +--> optional local Jev shadow export
                 |
                 v
          inert JSON envelope
                 |
                 X  no automatic network/model invocation
                 X  no result readback into Workbench
                 X  no permit/deny influence
```

The current branch does **not** invoke TypeSafe. It only creates a local candidate envelope when the operator explicitly sets:

`MATAWAKA_JEV_SHADOW_ROOT=<local directory>`

If the variable is absent or blank, the exporter returns `SKIPPED_DISABLED` and writes nothing.

## Why no direct TypeSafe call

Calling a remote model from the Workbench command path would itself create network/model-invocation effects. Shadow observation is not authority for those effects.

Therefore v0.1 separates:

`Workbench terminal result != shadow export authority != provider invocation authority`.

A later separately started sidecar may consume the local envelope only after an explicit externalization/sanitization design. The v0.1 envelope sets:

- `ExternalizationAuthorized=false`
- `ProviderInvocationAuthorized=false`
- `DecisionReadbackSupported=false`
- `AuthorityCreated=false`
- `DisplayPermitCreated=false`
- `ActionPermitCreated=false`

## Data minimization

The original command payload bytes are **not** included. Only `PayloadSha256` is exported.

For `agent.run`, the local envelope may contain the already-typed `CapabilityRequest` / `CapabilityDecision` projection needed for later research. This local file must not be treated as approved external-provider input; those fields can still contain sensitive operation/target text.

`Local shadow envelope != externalization permit`.

## Failure semantics

Shadow export occurs only after the base terminal state has been applied.

Exporter failure or cancellation:

- does not rerun the command;
- does not rerun the agent/provider;
- does not change `CapabilityDecision`;
- does not change terminal state;
- does not retry network/process/file effects from the command;
- only appends a local UI event describing the shadow-export failure.

## Protected authority sources

This branch must not modify:

- `src/Matawaka.Workbench.Runtime/CommandRouter.cs`
- `src/Matawaka.Workbench.AgentHost/DevelopmentAgentHost.cs`
- `src/Matawaka.Workbench.AgentHost/SemanticProvider.cs`
- `src/Matawaka.Workbench.Runtime/LiveCapabilityEvidenceAuditV058.cs`

The integration point is UI/application-side and post-terminal.

## Alpha.5 qualification basis

The bridge design is based on Jev adapter alpha.5 standard qualification:

- harness: `0.4.0-alpha.5`
- revision: `9d17b6c583b7acbb1a4a0524e5acaa046abeb1c9`
- observed model: `jev-1.13.0`
- 7 fixtures
- all 6 asserted Noul signals covered in both polarities
- 105 Noul oracle evaluations, 101 directional matches
- mean Brier loss ~0.03408
- all 4 misses localized to `operationalSpecificity`
- no material Choice permutation instability in the standard run

`operationalSpecificity` is therefore research evidence only and must not be used as a policy/admission threshold.

## Next gate

Before any TypeSafe sidecar is added:

1. compile and run the v0.1 qualification probe;
2. verify protected authority source SHAs remain unchanged;
3. verify local envelope contains no command payload bytes;
4. verify disabled mode writes nothing;
5. verify export failure cannot revise terminal state;
6. design a separate explicit externalization/sanitization boundary.

`Probabilistic Judgment != Authorization` remains structural.
