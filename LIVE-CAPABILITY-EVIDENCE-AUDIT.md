# Workbench v0.58 — live capability-evidence audit

v0.58 wires the already-qualified v0.57 provenance/capability composition into the real `agent.run` result path without making provenance an authority input.

## Causal order

```text
CommandRouter
  -> DevelopmentAgentHost.RunAsync
       -> CapabilityRequest
       -> FreeShieldReadOnlyCapabilityPolicy.Decide
       -> DENY / or provider with original CapabilityDecision
  <- DevelopmentAgentReceipt
  -> supplemental embedded provenance admission
  -> v0.57 ProvenanceCapabilityEvidenceComposer
  -> LiveCapabilityEvidenceAuditReceiptV058
  -> CommandResult.CapabilityEvidence
```

The audit occurs only after `DevelopmentAgentHost.RunAsync` returns. Therefore it cannot become provider execution authority for the operation it describes.

## Preserved authority contracts

`DevelopmentAgentHost.cs` is not changed by v0.58.

`SemanticProvider.cs` is not changed by v0.58.

The provider continues to receive the original `CapabilityDecision` produced by `FreeShieldReadOnlyCapabilityPolicy`.

`SemanticEvidencePacket.AuthorityReceipt` remains the existing base `CapabilityReceipt`; v0.58 does not add `CapabilityEvidenceCompositionReceipt` to SemanticHost stdin.

## Supplemental evidence

Runtime uses the exact existing Workbench provenance qualification as an assembly-embedded resource. It does not fetch, refresh, or resolve provenance at runtime.

Exact source identity remains:

- bytes: `2415`
- SHA-256: `ba2284c66ae4a48583a0918a1c7d4d6a96cf83e66c632b7d55a4aa0ec4d0b9c5`
- Git blob: `4ae93b36d5c8a6c2f00cbc53840221834e09ee26`
- admission decision: `PROVENANCE_OBSERVED_NO_AUTHORITY`

If supplemental evidence is unavailable or rejected, the audit reports `EVIDENCE_REJECTED_NO_AUTHORITY_CHANGE`. It does not become a new allow requirement and cannot upgrade the independently produced base decision.

A valid attachment reports `COMPOSED_NO_AUTHORITY_CHANGE` and carries the v0.57 composition receipt.

## Invariants

```text
Evidence attached != Authority granted
Audit receipt != ActionPermit
Provenance != Truth
Provenance != Permission
Base deny + provenance = deny
Provider authority = original base CapabilityDecision
SemanticHost authority input = existing base CapabilityReceipt
Missing supplemental provenance != new denial authority
Rejected supplemental provenance != new allow authority
Trigger != Authorization
```

v0.58 creates no network, arbitrary-process, Git, file-mutation, model/runtime, response/display, action, successor, release-publication, or accepted-tag authority.
