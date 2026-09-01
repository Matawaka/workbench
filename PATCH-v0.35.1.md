# Matawaka Workbench v0.35.1 — Lifecycle Version-Key Stabilization

Status: patch candidate over exact accepted/published v0.35. No new product feature or authority.

## Exact predecessor

- commit `689cdf5ef2f9f403efe09bb251c91da1c5951ec6`
- tag `workbench-v0.35-accepted`
- parent `c69d3237bae06b80481ce2421eb34e8cf1a88c1b`

## Observed defect

v0.35 Self-test, local checkpoint and publication succeeded, but post-publication Lifecycle receipt failed closed:

`Lifecycle checkpoint binding is missing: candidates=0`

The checkpoint was not missing. The accepted generic lifecycle adapter used the accepted tag token as if it were the full semantic Workbench version.

v0.35 evidence intentionally contains:

```text
accepted tag          workbench-v0.35-accepted
schema token          v0.35
semantic Version      0.35.0
```

The old condition `checkpoint.Version == tagToken` therefore excluded the correct checkpoint.

## Stabilized model

`Accepted Tag/Schema Version != Semantic Runtime Version`

`MaintenanceLifecycleReceiptV2Service` separates the two identities.

### Accepted tag/schema token

Derived from the unique accepted tag at HEAD and used for receipt schema identity:

- `workbench-v0.35-accepted` -> token `0.35`;
- schemas use `/v0.35`.

### Semantic runtime version

Derived from the unique checkpoint only after exact checkpoint selection by:

- checkpoint schema `/v<tag-token>`;
- exact target tag;
- exact predecessor HEAD;
- exact accepted HEAD.

Then semantic Version must normalize back to the tag/schema token.

Normalization is explicit:

```text
0.34.1 -> 0.34.1
0.35.0 -> 0.35
0.35.1 -> 0.35.1
```

Only a zero patch component is omitted. A non-zero patch may not collapse to the minor token.

## Lifecycle v2 binding

After version-key resolution the previous exact bindings remain:

- checkpoint-bound acceptance path + SHA-256;
- passing acceptance schema token + semantic Version;
- candidate executable digest across orchestrator / acceptance / checkpoint;
- unique orchestrator target semantic Version/tag/predecessor;
- unique publication schema token + semantic Version + exact local/remote refs;
- clean current Workbench source state.

Missing or ambiguous evidence still fails closed. No artifact is selected by modification time.

## Historical preservation

The accepted v0.34.1 `MaintenanceLifecycleReceiptService` remains in source as historical evidence of the adapter that qualified successfully on v0.34.1 and exposed the v0.35 version-key regression.

The active lifecycle button in v0.35.1 uses `MaintenanceLifecycleReceiptV2Service`.

`Successor Adapter != Historical Evidence Rewrite`

## Acceptance successor

v0.35.1 adds only:

- Self-test `0.35.1` = accepted v0.35 matrix + executable lifecycle-v2 normalization regression checks;
- local target `workbench-v0.35.1-accepted` over exact accepted v0.35;
- fixed `Matawaka/workbench` fast-forward/tag publisher for v0.35.1;
- Lifecycle receipt button wired to lifecycle v2.

The visible product surface remains eight buttons and zero persistent authority checkboxes. Local application maintenance is unchanged.

## Non-effects

No local-app update or registration expansion, no automatic lifecycle action, no general filesystem/network/Git/catalog/AgentExecute authority, no Runtime/Protocol/AgentHost/Engine/Catalog/SemanticHost change, no canonical UU-AAP conformance or Stable Core promotion.
