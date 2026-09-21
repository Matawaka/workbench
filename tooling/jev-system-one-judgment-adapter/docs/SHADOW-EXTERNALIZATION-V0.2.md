# Jev shadow externalization candidate v0.2

Status: **offline candidate construction only / no network send / no provider invocation**.

v0.1 proved that Workbench can produce a local inert shadow envelope without changing authority. v0.2 adds the next boundary: a separate operator-reviewed sanitized/synthetic disclosure is bound to that exact shadow envelope and converted into a provider request **candidate**.

The candidate is not a send permit.

## Required separation

```text
raw Workbench shadow envelope
        |
        | local digest binding only
        v
operator-reviewed synthetic disclosure
        |
        v
sanitized Jev request candidate
        |
        X no network send
        X no TypeSafe call
        X no Workbench authority readback
```

The disclosure must explicitly set `operatorAttestedSanitized=true`. The builder also rejects exact reuse of raw command id/target, authority subject, raw operation, or raw authority target. This is a narrow leak detector, **not** a proof that the synthetic disclosure is non-sensitive.

Every candidate preserves:

- `normativeEffect=NONE`
- `authorityIssuance=OUT_OF_SCOPE`
- `externalizationAuthorized=false`
- `providerInvocationAuthorized=false`
- `networkSendAuthorized=false`
- `decisionReadbackSupported=false`
- `humanReviewRequired=true`

`operationalSpecificity` is explicitly marked `RESEARCH_ONLY` based on alpha.5 standard evidence.

## Non-effects

Candidate construction does not authorize externalization, network access, provider/model invocation, authority creation, permits, execution, or result readback.

The next frontier, if separately approved, is a one-shot sidecar send authority over one exact reviewed candidate digest. That must be a new capability and a new review.
