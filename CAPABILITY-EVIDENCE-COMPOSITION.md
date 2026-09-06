# Workbench v0.57 — Provenance Capability Evidence Composition

## Purpose

v0.57 composes an already-admitted provenance observation with an independently produced Workbench capability decision without letting provenance mint or widen authority.

```text
accepted provenance admission
        ↓
CapabilityEvidenceContext
        +
existing CapabilityRequest
        +
existing CapabilityDecision
        ↓
CapabilityEvidenceCompositionReceipt
```

The existing `FreeShieldReadOnlyCapabilityPolicy` remains the authority decision source. v0.57 does not replace it.

## Core invariant

**Evidence admitted != Authority granted.**

The composition service is monotonic with respect to authority:

- base `deny` remains `deny`;
- base `allow/read-only` remains exactly `allow/read-only` only when it already satisfies the existing policy envelope;
- mutation budget cannot increase;
- network access cannot become true;
- arbitrary process execution cannot become true;
- provenance cannot create model/runtime/response/display/action/successor authority.

Invalid or drifted evidence/base decisions fail closed before a composition receipt is produced.

## Exact provenance class

The only admitted provenance class is the already accepted v0.56 identity:

`PROVENANCE_OBSERVED_NO_AUTHORITY`

The evidence binding remains the exact accepted uu-aap #955 qualification already embedded in Workbench. v0.57 does not retrieve, refresh, rewrite, or reinterpret it.

## Existing policy preserved

`FreeShieldReadOnlyCapabilityPolicy` still independently decides requests. Its accepted allow envelope remains:

- operation: `observe` or `propose`;
- authority: `read-only`;
- mutation budget: `0`;
- network access: `false`;
- arbitrary process execution: `false`.

`execute` remains denied.

## Non-effects

v0.57 does not:

- perform network access;
- start a process;
- invoke a model/runtime;
- mutate repositories or files;
- create response/display authority;
- create an ActionPermit or SuccessorPermit;
- publish or modify Git refs;
- reinterpret an unsigned Git tag as signed;
- create truth or publication authority from provenance.

The accepted/public predecessor is `workbench-v0.56.2-accepted` at `693886052c598e9ed02558ca60a1fd8901090236`.
