# Workbench v0.60 — live Authority / Evidence surface

## Status

Bounded display-only successor over the merged v0.59 human-reviewed presentation.

The v0.60 change does not define a new authority policy, evidence semantics, runtime audit, provider contract, or accepted release identity.

## Exact predecessor

The implementation branch is based on canonical post-v0.59 Workbench main:

- commit `a17a8b064c0b07271cbeb61e48d7bc85e7bc8554`
- tree `999a3f9348e955fae9069d229c67da40ba0e5498`

The accepted/public release identity remains `workbench-v0.56.2-accepted`.

## Live display chain

```text
CommandRouter / DevelopmentAgentHost already complete or deny
        ↓
CommandResult.Authority = CapabilityReceipt
CommandResult.CapabilityEvidence = LiveCapabilityEvidenceAuditReceiptV058
        ↓
MainWindow.RenderResult
        ↓
TryShowLiveCapabilityEvidenceV060
        ↓
existing human-reviewed v0.59 presentation service + UI builder
        ↓
Authority / Evidence tab
```

The display chain starts only after `ICommandRunner.RunAsync` has returned. It cannot affect the operation it describes.

## Reused human-reviewed semantics

v0.60 does not fork the v0.59 projection. It calls the same `ShowCapabilityEvidenceReviewV059(authority, audit)` method and therefore retains the already reviewed Russian/English distinction:

`POLICY AUTHORITY != EVIDENCE AUDIT`

Human-facing evidence remains contextual/constraint evidence and not an allow source.

## Typed activation boundary

The live tab is created only when both are present as the exact expected typed receipts:

- `CommandResult.Authority is CapabilityReceipt`
- `CommandResult.CapabilityEvidence is LiveCapabilityEvidenceAuditReceiptV058`

If either is absent, no live Authority / Evidence statement is invented.

The v0.59 validator continues to reject mismatched request/decision bindings and any receipt that reports authority promotion.

## Failure behavior

Presentation is downstream of the already-produced `CommandResult`.

A presentation failure:

- does not rerun `CommandRouter`;
- does not rerun `DevelopmentAgentHost` or provider;
- does not invoke SemanticHost;
- does not alter the already-produced terminal state;
- does not replace the base authority receipt;
- does not retry network/process/Git/file operations;
- removes any stale Authority / Evidence tab and falls back to existing result-tab selection.

## Protected sources

Independent qualification requires these merged predecessor sources to remain byte-identical:

- `CommandRouter.cs`
- `DevelopmentAgentHost.cs`
- `SemanticProvider.cs`
- `ProvenanceCapabilityEvidenceComposer.cs`
- `LiveCapabilityEvidenceAuditV058.cs`
- `CapabilityEvidenceReviewV059.cs`

Only UI display wiring is allowed in v0.60.

## Qualification target

The Windows qualification must prove:

1. exact post-v0.59 predecessor SHA/tree;
2. accepted v0.56.2 continuity;
3. exact v0.59 human-reviewed qualification run/artifact and merged predecessor;
4. protected authority/runtime/reviewed-presentation source identity;
5. no `workbench-v0.60*` tag;
6. full Release build;
7. real live `CommandRouter` allow result produces typed base authority + supplemental audit;
8. the exact v0.59 RU/EN projection is reused over those real receipts;
9. base deny remains deny and provider remains uncalled;
10. non-agent result does not invent the live surface;
11. promoted/mismatched audit is rejected by the same v0.59 validator;
12. display wiring occurs only after runner return and contains no execution/effect path;
13. clean repository boundary.

## Human gate

No new visual design is introduced in v0.60. The same v0.59 presentation service and UI builder that already passed human review are reused.

A second visual design review is required only if qualification finds that v0.60 changed the v0.59 projection/layout or if later testing demonstrates that the live placement materially changes human interpretation.

## Non-effects

`Live display != Authority creation`

`Evidence displayed != Permission`

`UI selection != Authorization`

`Audit status != ActionPermit`

`Origin evidence != Truth`

Merging this source delta alone creates no accepted v0.60 release identity, publication authority, model/runtime invocation authority, network/process authority, ResponseAuthority, DisplayPermit, ActionPermit or SuccessorPermit.
