# Matawaka Workbench v0.31 - Revocation Inference Refusal & Historical Evidence Preservation

Status: additive Workbench maintenance/evidence layer. No UU-AAP Stable Core promotion.

## Purpose

v0.30 demonstrated one fixture cryptographic predecessor-to-successor key relation while explicitly preserving:

- `PredecessorRevocationProven=false`;
- `TrustedTimestampValidated=false`;
- `TrustedTemporalOrderingProven=false`;
- `DelegationAuthorityGranted=false`;
- `SuccessorOperationalAuthorityGranted=false`.

v0.31 makes those negative facts operationally explicit.

The boundary refuses to infer effective predecessor-key revocation merely because a successor key exists or because a rotation relation is observed. It also re-verifies the exact historical v0.29 predecessor-key signature and preserves that historical evidence as evidence.

The boundary does not decide whether the predecessor key should be accepted or rejected for future use.

## Exact predecessor frontier

- accepted commit: `1c12f1f51b2a03cf45b2ca792a5e5315b6fc61f3`
- accepted tag: `workbench-v0.30-accepted`
- exact v0.30 Key continuity receipt SHA-256:
  `2c4270fc6bf18bf29251d893d3539dcb4afd97e45152b5ec311fa3ce210a2f7d`
- predecessor key fingerprint:
  `1048a67242e8d24db9fb900ae1d54275710831623b0ad30c811030a2bb86c734`
- successor key fingerprint:
  `ccce3e9dc674eac4633d348f1c19c307b1b55730974875c9e733e24f1a4e53ea`

Historical v0.29 evidence:

- historical claim SHA-256:
  `94ddcb67ee4e3ac3cfd3fa5cc2e0af24ca46975b3f50516de66889d60282eaba`
- historical detached-signature SHA-256:
  `0123a4f6ed55a8ce9b67d55d736359661204b3d5218f1330ea375009b3a631a0`

## New surface

After `workbench-v0.31-accepted`, the GUI exposes **Revocation boundary**.

The action:

1. requires clean accepted v0.31 at HEAD;
2. locates the exact retained v0.30 continuity receipt by fixed SHA-256;
3. revalidates the v0.30 bounded continuity contract;
4. parses the exact rotation claim and requires `PredecessorRevocationClaimed=false`;
5. refuses revocation inference from rotation alone;
6. refuses revocation inference from successor possession alone;
7. refuses trusted-time inference from the ordinal relation;
8. re-verifies the exact historical v0.29 predecessor signature;
9. requires historical claim-byte drift to fail;
10. requires successor-key substitution for the historical signature to fail;
11. preserves historical evidence while future predecessor-key policy remains unresolved;
12. verifies main Git state unchanged;
13. writes one bounded receipt.

No signing, key revocation, key activation, policy mutation, registry mutation, or historical evidence mutation occurs.

## Expected receipt

Schema:

`matawaka.workbench-producer-key-revocation-inference-boundary/v0.31`

Passing status:

`REFUSED_REVOCATION_INFERENCE_PRESERVED_HISTORICAL_EVIDENCE_FUTURE_POLICY_UNRESOLVED`

Required aggregate:

- `Passed=true`
- `SourceContinuityVerified=true`
- `RotationClaimVerified=true`
- `RotationClaimExplicitlyDoesNotClaimRevocation=true`
- `RotationAloneRevocationInferenceRefused=true`
- `SuccessorPossessionRevocationInferenceRefused=true`
- `OrdinalTrustedTimeInferenceRefused=true`
- `HistoricalSignatureVerified=true`
- `HistoricalClaimByteDriftRefused=true`
- `HistoricalPublicKeySubstitutionRefused=true`
- `HistoricalEvidencePreserved=true`
- `HistoricalEvidenceInvalidated=false`
- `PredecessorRevocationProven=false`
- `TrustedTemporalOrderingProven=false`
- `FuturePredecessorAcceptanceAuthorized=false`
- `FuturePredecessorRejectionAuthorized=false`
- `RevocationEnforcementAuthorized=false`
- `KeyRegistryMutationAuthorized=false`
- `AuthorityExpansionDetected=false`
- `MainRepositoryUnchanged=true`

## Strengthened invariants

```text
Rotation Evidence != Revocation Evidence.
Revocation Evidence != Revocation Enforcement Authority.
Future Key Policy != Historical Evidence Validity.
Historical Evidence Preservation != Future Key Acceptance.
Ordinal Relation != Trusted Time.
Successor Continuity != Predecessor Erasure.
```

## Authority boundary

Allowed effects are limited to exact retained-receipt reads/hashes, public-key verification, in-memory negative checks, a local no-authority policy classification, and one receipt write.

The action does not authorize or perform:

- private-key access or signing;
- key revocation or activation;
- key/certificate registry mutation;
- trusted time or trust establishment;
- producer identity/authentication/common-controller claims;
- future predecessor-key acceptance or rejection;
- historical evidence deletion or invalidation;
- source mutation/build/checkpoint;
- git fetch/push or remote mutation;
- network/catalog mutation;
- Agent Execute or ActionPermit creation;
- portability claims;
- canonical UU-AAP conformance;
- Stable Core/interface-registry promotion.

## Acceptance sequencing

Self-test does not run Revocation boundary.

First update from accepted v0.30 to the v0.31 candidate through the ordinary package -> materialize -> apply plan -> apply+build -> candidate launch path. Run a passing v0.31 Self-test and explicitly accept `workbench-v0.31-accepted`.

Only then run **Revocation boundary** with its separate confirmation dialog.
